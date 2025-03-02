﻿using System;
using System.Collections;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DatabaseAPI;
using Firebase.Auth;
using Firebase.Extensions;
using Lobby;
using Managers;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public class GameData : MonoBehaviour
{
	private static GameData gameData;

	[Tooltip(
		"Only use this when offline or you can't reach the auth server! Allows the game to still work in that situation and " +
		" allows skipping login. Host player will also be given admin privs." +
		"Not supported in release builds.")]
	[SerializeField]
	private bool offlineMode = false;

	/// <summary>
	/// Whether --offlinemode command line argument is passed. Enforces offline mode.
	/// </summary>
	private bool forceOfflineMode;

	/// <summary>
	/// Is offline mode enabled, allowing login skip / working without connection to server.?
	/// Disabled always for release builds.
	/// </summary>
	public bool OfflineMode => (!BuildPreferences.isForRelease && offlineMode) || forceOfflineMode;

	public bool testServer;
	private RconManager rconManager;

	/// <summary>
	///     Check to see if you are in the game or in the lobby
	/// </summary>
	public static bool IsInGame { get; private set; }

	public static bool IsHeadlessServer
	{
		get { return GameInfo.IsHeadlessServer; }
		private set { GameInfo.IsHeadlessServer = value; }
	}

	public static string LoggedInUsername { get; set; }

	public static int BuildNumber { get; private set; }
	public static string ForkName { get; private set; }

	public static GameData Instance
	{
		get
		{
			if (!gameData)
			{
				gameData = FindObjectOfType<GameData>();
			}

			return gameData;
		}
	}

	public bool DevBuild = false;

	#region Lifecycle

	private void Start()
	{
		Init();
	}

	public async void APITest()
	{
		var url = "https://api.unitystation.org/validatetoken?data=";

		HttpRequestMessage r = new HttpRequestMessage(HttpMethod.Get,
			url + UnityWebRequest.EscapeURL(JsonUtility.ToJson("")));

		CancellationToken cancellationToken = new CancellationTokenSource(120000).Token;

		HttpResponseMessage res;
		try
		{
			res = await ServerData.HttpClient.SendAsync(r, cancellationToken);
		}
		catch (System.Net.Http.HttpRequestException e)
		{
			forceOfflineMode = true;
			return;
		}

		forceOfflineMode = false;
	}


	private void Init()
	{
#if UNITY_EDITOR
		DevBuild = true;
#endif
		var buildInfo =
			JsonUtility.FromJson<BuildInfo>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath,
				"buildinfo.json")));
		BuildNumber = buildInfo.BuildNumber;
		ForkName = buildInfo.ForkName;
		forceOfflineMode = !string.IsNullOrEmpty(GetArgument("-offlinemode"));
		Logger.Log($"Build Version is: {BuildNumber}. " + (OfflineMode ? "Offline mode" : string.Empty));
		CheckHeadlessState();
		APITest();
		Environment.SetEnvironmentVariable("MONO_REFLECTION_SERIALIZER", "yes");

		string testServerEnv = Environment.GetEnvironmentVariable("TEST_SERVER");
		if (!string.IsNullOrEmpty(testServerEnv))
		{
			testServer = Convert.ToBoolean(testServerEnv);
		}

		if (!CheckCommandLineArgs())
		{
			if (FirebaseAuth.DefaultInstance.CurrentUser != null)
			{
				if (LobbyManager.Instance.OrNull()?.lobbyDialogue != null)
				{
					AttemptAutoJoin(LobbyManager.Instance.lobbyDialogue.LoginSuccess);
				}
				else
				{
					Logger.LogWarning("LobbyManager.Instance == null");
				}

			}
		}
	}

	private void OnEnable()
	{
		Logger.RefreshPreferences();

		SceneManager.activeSceneChanged += OnLevelFinishedLoading;
	}

	private void OnDisable()
	{
		SceneManager.activeSceneChanged -= OnLevelFinishedLoading;
	}

	private IEnumerator WaitToStartServer()
	{
		yield return WaitFor.Seconds(0.1f);
		CustomNetworkManager.Instance.StartHost();
	}

	private void OnLevelFinishedLoading(Scene oldScene, Scene newScene)
	{
		Resources.UnloadUnusedAssets();
		if (newScene.name == "Lobby")
		{
			IsInGame = false;
			GameScreenManager.Instance.SetScreenForLobby();
		}
		else
		{
			IsInGame = true;
			GameScreenManager.Instance.SetScreenForGame();
		}

		if (CustomNetworkManager.Instance.isNetworkActive)
		{
			//Reset stuff
			CheckHeadlessState();

			if (IsInGame && GameManager.Instance != null && CustomNetworkManager.Instance._isServer)
			{
				GameManager.Instance.ResetRoundTime();
			}

			return;
		}

		//Check if running in batchmode (headless server)
		if (CheckHeadlessState())
		{
			//			float calcFrameRate = 1f / Time.deltaTime;
			//			Application.targetFrameRate = (int) calcFrameRate;
			//			Logger.Log($"Starting server in HEADLESS mode. Target framerate is {Application.targetFrameRate}",
			//				Category.Server);

			Logger.Log($"FrameRate limiting has been disabled on Headless Server",
				Category.Server);
			IsHeadlessServer = true;
			StartCoroutine(WaitToStartServer());

			if (rconManager == null)
			{
				GameObject rcon = Instantiate(Resources.Load("Rcon/RconManager") as GameObject, null) as GameObject;
				rconManager = rcon.GetComponent<RconManager>();
				Logger.Log("Start rcon server", Category.Rcon);
			}
		}
	}

	#endregion

	private bool CheckCommandLineArgs()
	{
		//Check for Hub Message
		string serverIp = GetArgument("-server");
		string port = GetArgument("-port");
		string token = GetArgument("-refreshtoken");
		string uid = GetArgument("-uid");

		//This is a hub message, attempt to login and connect to server
		if (!string.IsNullOrEmpty(serverIp) && !string.IsNullOrEmpty(port))
		{
			HubToServerConnect(serverIp, port, uid, token);
			return true;
		}

		return false;
	}

	private async void AttemptAutoJoin(Action<string> OnLoginSuccess)
	{
		await Task.Delay(TimeSpan.FromSeconds(0.1));

		if (LobbyManager.Instance == null) return;

		LobbyManager.Instance.lobbyDialogue.ShowLoggingInStatus(
			$"Loading user profile for {FirebaseAuth.DefaultInstance.CurrentUser.DisplayName}");

		await FirebaseAuth.DefaultInstance.CurrentUser.TokenAsync(true).ContinueWithOnMainThread(
			task => {
				if (task.IsCanceled || task.IsFaulted)
				{
					LobbyManager.Instance.lobbyDialogue.LoginError(task.Exception?.Message);
					return;
				}
			});

		await ServerData.ValidateUser(FirebaseAuth.DefaultInstance.CurrentUser,
			OnLoginSuccess,
			LobbyManager.Instance.lobbyDialogue.LoginError);
	}

	private async void HubToServerConnect(string ip, string port, string uid, string token)
	{
		await Task.Delay(TimeSpan.FromSeconds(0.1));

		LobbyManager.Instance.lobbyDialogue.serverAddressInput.text = ip;
		LobbyManager.Instance.lobbyDialogue.serverPortInput.text = port;

		GameScreenManager.Instance.serverIP = ip;

		if (string.IsNullOrEmpty(token) == false)
		{
			LobbyManager.Instance.lobbyDialogue.ShowLoggingInStatus("Verifying account details..");
			var refreshToken = new RefreshToken();
			refreshToken.refreshToken = token;
			refreshToken.userID = uid;

			var response = await ServerData.ValidateToken(refreshToken);

			if (response == null)
			{
				LobbyManager.Instance.lobbyDialogue.LoginError(
					$"Unknown server error. Please check your logs for more information by press F5");
				return;
			}

			if (string.IsNullOrEmpty(response.errorMsg) == false)
			{
				Logger.LogError($"Something went wrong with hub token validation {response.errorMsg}", Category.DatabaseAPI);
				LobbyManager.Instance.lobbyDialogue.LoginError($"Could not verify your details {response.errorMsg}");
				return;
			}

			await FirebaseAuth.DefaultInstance.SignInWithCustomTokenAsync(response.message).ContinueWithOnMainThread(
				async task =>
				{
					if (task.IsCanceled)
					{
						Logger.LogError("Custom token sign in was canceled.", Category.DatabaseAPI);
						LobbyManager.Instance.lobbyDialogue.LoginError($"Sign in was cancelled");
						return;
					}

					if (task.IsFaulted)
					{
						Logger.LogError("Task Faulted: " + task.Exception, Category.DatabaseAPI);
						LobbyManager.Instance.lobbyDialogue.LoginError($"Task Faulted: " + task.Exception);
						return;
					}

					var success = await ServerData.ValidateUser(task.Result, null, null);

					if (success)
					{
						Logger.Log("Signed in successfully with valid token", Category.DatabaseAPI);
						OnCharacterScreenCloseFromHubConnect();
					}
					else
					{
						LobbyManager.Instance.lobbyDialogue.LoginError(
							"Unknown error occured when verifying character settings on the server");
					}
				});
		}
		else
		{
			if (FirebaseAuth.DefaultInstance.CurrentUser != null)
			{
				AttemptAutoJoin(OnCharacterScreenCloseFromHubConnect);
			}
		}
	}

	private void OnCharacterScreenCloseFromHubConnect()
	{
		LobbyManager.Instance.lobbyDialogue.OnStartGameFromHub();
	}

	private void OnCharacterScreenCloseFromHubConnect(string msg)
	{
		LobbyManager.Instance.lobbyDialogue.OnStartGameFromHub();
	}

	private bool CheckHeadlessState()
	{
		if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || Instance.testServer)
		{
			IsHeadlessServer = true;
			return true;
		}

		return false;
	}

	private string GetArgument(string name)
	{
		string[] args = Environment.GetCommandLineArgs();
		for (int i = 0; i < args.Length; i++)
		{
			if (args[i].Contains(name))
			{
				return args[i + 1];
			}
		}

		return null;
	}
}
