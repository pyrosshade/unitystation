using System;
using UnityEngine;
using Random = UnityEngine.Random;
using Mirror;
using ScriptableObjects;
using Light2D;
using Systems.Electricity;
using Systems.Explosions;
using Systems.ObjectConnection;
using Objects.Construction;


namespace Objects.Lighting
{
	/// <summary>
	/// Component responsible for the behaviour of light tubes / bulbs in particular.
	/// </summary>
	public class LightSource : ObjectTrigger, ICheckedInteractable<HandApply>, IAPCPowerable, IServerLifecycle,
		IMultitoolSlaveable
	{
		public Color ONColour;
		public Color EmergencyColour;

		public LightSwitchV2 relatedLightSwitch;

		[SerializeField] private LightMountState InitialState = LightMountState.On;

		[SyncVar(hook = nameof(SyncLightState))]
		private LightMountState mState;

		[Header("Generates itself if this is null:")]
		public GameObject mLightRendererObject;

		[SerializeField] private bool isWithoutSwitch = true;
		public bool IsWithoutSwitch => isWithoutSwitch;
		private bool switchState = true;
		private PowerState powerState;

		[SerializeField] private SpriteHandler spriteHandler;
		[SerializeField] private SpriteRenderer spriteRendererLightOn;
		private LightSprite lightSprite;
		[SerializeField] private EmergencyLightAnimator emergencyLightAnimator = default;
		[SerializeField] private Integrity integrity = default;
		[SerializeField] private Rotatable directional;

		[SerializeField] private BoxCollider2D boxColl = null;
		[SerializeField] private Vector4 collDownSetting = Vector4.zero;
		[SerializeField] private Vector4 collRightSetting = Vector4.zero;
		[SerializeField] private Vector4 collUpSetting = Vector4.zero;
		[SerializeField] private Vector4 collLeftSetting = Vector4.zero;
		[SerializeField] private SpritesDirectional spritesStateOnEffect = null;
		[SerializeField] private SOLightMountStatesMachine mountStatesMachine = null;
		[SerializeField, Range(0, 100f)] private float maximumDamageOnTouch = 3f;

		[SerializeField] private GameObject sparkObject = null;

		private SOLightMountState currentState;
		private ObjectBehaviour objectBehaviour;
		private LightFixtureConstruction construction;

		private ItemTrait traitRequired;
		private GameObject itemInMount;

		public float integrityThreshBar { get; private set; }

		#region Lifecycle

		private void Awake()
		{
			objectBehaviour = GetComponent<ObjectBehaviour>();
			construction = GetComponent<LightFixtureConstruction>();
			if (mLightRendererObject == null)
			{
				mLightRendererObject = LightSpriteBuilder.BuildDefault(gameObject, new Color(0, 0, 0, 0), 12);
			}

			lightSprite = mLightRendererObject.GetComponent<LightSprite>();
			if (isWithoutSwitch == false)
			{
				switchState = InitialState == LightMountState.On;
			}

			ChangeCurrentState(InitialState);
			traitRequired = currentState.TraitRequired;
			RefreshBoxCollider();
		}

		private void OnEnable()
		{
			directional.OnRotationChange.AddListener(OnDirectionChange);
			integrity.OnApplyDamage.AddListener(OnDamageReceived);
		}

		private void OnDisable()
		{
			directional.OnRotationChange.RemoveListener(OnDirectionChange);
			if (integrity != null) integrity.OnApplyDamage.RemoveListener(OnDamageReceived);

			UpdateManager.Remove(CallbackType.PERIODIC_UPDATE, TrySpark);
		}

		public void OnSpawnServer(SpawnInfo info)
		{
			if (info.SpawnItems == false)
			{
				mState = LightMountState.MissingBulb;
			}
		}

		public void OnDespawnServer(DespawnInfo info)
		{
			Spawn.ServerPrefab(currentState.LootDrop, gameObject.RegisterTile().WorldPositionServer);
			UnSubscribeFromSwitchEvent();
		}

		#endregion

		#region Multitool Interaction

		MultitoolConnectionType IMultitoolLinkable.ConType => MultitoolConnectionType.LightSwitch;
		IMultitoolMasterable IMultitoolSlaveable.Master => relatedLightSwitch;
		bool IMultitoolSlaveable.RequireLink => false;

		bool IMultitoolSlaveable.TrySetMaster(PositionalHandApply interaction, IMultitoolMasterable master)
		{
			SetMaster(master);
			return true;
		}

		void IMultitoolSlaveable.SetMasterEditor(IMultitoolMasterable master)
		{
			SetMaster(master);
		}

		private void SetMaster(IMultitoolMasterable master)
		{
			if (master is LightSwitchV2 lightSwitch && lightSwitch != relatedLightSwitch)
			{
				SubscribeToSwitchEvent(lightSwitch);
			}
			else if (relatedLightSwitch != null)
			{
				UnSubscribeFromSwitchEvent();
			}
		}

		#endregion

		private void OnDirectionChange(OrientationEnum newDir)
		{
			SetSprites();
		}

		[Server]
		public void ServerChangeLightState(LightMountState newState)
		{
			mState = newState;

			if (newState == LightMountState.Broken)
			{
				UpdateManager.Add(TrySpark, 1f);
			}
			else
			{
				UpdateManager.Remove(CallbackType.PERIODIC_UPDATE, TrySpark);
			}
		}

		public bool HasBulb()
		{
			return mState != LightMountState.MissingBulb && mState != LightMountState.None;
		}

		private void SyncLightState(LightMountState oldState, LightMountState newState)
		{
			mState = newState;
			ChangeCurrentState(newState);
			SetSprites();
			SetAnimation();
		}

		private void ChangeCurrentState(LightMountState newState)
		{
			if (mountStatesMachine.LightMountStates.Contains(newState))
			{
				currentState = mountStatesMachine.LightMountStates[newState];
			}
		}

		public void EditorDirectionChange()
		{
			directional = GetComponent<Rotatable>();
			spriteRendererLightOn = GetComponentsInChildren<SpriteRenderer>().Length > 1
				? GetComponentsInChildren<SpriteRenderer>()[1]
				: GetComponentsInChildren<SpriteRenderer>()[0];
			var state = mountStatesMachine.LightMountStates[LightMountState.On];

			spriteHandler.SetSpriteSO(state.SpriteData, null);
			spriteRendererLightOn.sprite = spritesStateOnEffect.sprites[0];
			RefreshBoxCollider();
		}

		public void RefreshBoxCollider()
		{
			directional = GetComponent<Rotatable>();
			Vector2 offset = Vector2.zero;
			Vector2 size = Vector2.zero;


			offset = new Vector2(collUpSetting.x, collUpSetting.y);
			size = new Vector2(collUpSetting.z, collUpSetting.w);

			boxColl.offset = offset;
			boxColl.size = size;
		}

		private void SetSprites()
		{
			spriteHandler.SetSpriteSO(currentState.SpriteData, null);
			spriteRendererLightOn.sprite = mState == LightMountState.On
				? spritesStateOnEffect.sprites[0]
				: null;

			itemInMount = currentState.Tube;

			var currentMultiplier = currentState.MultiplierIntegrity;
			if (currentMultiplier > 0.15f)
			{
				integrityThreshBar = integrity.initialIntegrity * currentMultiplier;
			}

			RefreshBoxCollider();
		}

		private void SetAnimation()
		{
			lightSprite.Color = currentState.LightColor;
			switch (mState)
			{
				case LightMountState.Emergency:
					lightSprite.Color = EmergencyColour;
					mLightRendererObject.transform.localScale = Vector3.one * 3.0f;
					mLightRendererObject.SetActive(true);
					if (emergencyLightAnimator != null)
					{
						emergencyLightAnimator.StartAnimation();
					}

					break;
				case LightMountState.On:
					if (emergencyLightAnimator != null)
					{
						emergencyLightAnimator.StopAnimation();
					}

					lightSprite.Color = ONColour;
					mLightRendererObject.transform.localScale = Vector3.one * 12.0f;
					mLightRendererObject.SetActive(true);
					break;
				default:
					if (emergencyLightAnimator != null)
					{
						emergencyLightAnimator.StopAnimation();
					}

					mLightRendererObject.transform.localScale = Vector3.one * 12.0f;
					mLightRendererObject.SetActive(false);
					break;
			}
		}

		#region ICheckedInteractable<HandApply>

		public bool WillInteract(HandApply interaction, NetworkSide side)
		{
			if (!DefaultWillInteract.Default(interaction, side)) return false;
			if (!construction.IsFullyBuilt()) return false;
			if (interaction.HandObject != null && interaction.Intent == Intent.Harm) return false;
			if (interaction.HandObject != null &&
			    !Validations.HasItemTrait(interaction.HandObject, CommonTraits.Instance.LightReplacer) &&
			    !Validations.HasItemTrait(interaction.HandObject, traitRequired)) return false;

			return true;
		}

		public void ServerPerformInteraction(HandApply interaction)
		{
			if (interaction.HandObject == null)
			{
				TryRemoveBulb(interaction);
			}
			else if (Validations.HasItemTrait(interaction.HandObject, CommonTraits.Instance.LightReplacer))
			{
				TryReplaceBulb(interaction);
			}
			else if (Validations.HasItemTrait(interaction.HandObject, traitRequired))
			{
				TryAddBulb(interaction);
			}
		}

		private void TryRemoveBulb(HandApply interaction)
		{
			try
			{
				//(Gilles)  : the hand that we use to interact and hold items isn't the same entity as the slot where you wear gloves.
				//(MaxIsJoe): GetActiveHand() retrieves the slot you hold and use items with not the slot that you use to wear gloves.
				//TODO : According to Gilles this is conceptually wrong and should be dealt with sometime in the future.
				var handSlots = interaction.PerformerPlayerScript.DynamicItemStorage.GetNamedItemSlots(NamedSlot.hands);

				bool HasGlove()
				{
					foreach (var slot in handSlots)
					{
						if (slot.IsEmpty) continue;
						if (Validations.HasItemTrait(slot.ItemObject, CommonTraits.Instance.BlackGloves)) return true;
					}

					return false;
				}

				if (mState == LightMountState.On && HasGlove() == false)
				{
					float damage = Random.Range(0, maximumDamageOnTouch);
					var playerHealth = interaction.PerformerPlayerScript.playerHealth;
					var burntBodyPart = interaction.HandSlot.NamedSlot == NamedSlot.leftHand
						? BodyPartType.LeftArm
						: BodyPartType.RightArm;
					playerHealth.ApplyDamageToBodyPart(gameObject, damage, AttackType.Energy, DamageType.Burn,
						burntBodyPart);

					Chat.AddExamineMsgFromServer(interaction.Performer,
						"<color=red>You burn your hand on the bulb while attempting to remove it!</color>");
					return;
				}

				var spawnedItem = Spawn.ServerPrefab(itemInMount, interaction.Performer.WorldPosServer()).GameObject;
				ItemSlot bestHand = interaction.PerformerPlayerScript.DynamicItemStorage.GetBestHand();
				if (bestHand != null && spawnedItem != null)
				{
					Inventory.ServerAdd(spawnedItem, bestHand);
				}

				ServerChangeLightState(LightMountState.MissingBulb);
			}
			catch (NullReferenceException exception)
			{
				Logger.LogError(
					$"A NRE was caught in LightSource.TryRemoveBulb(): {exception.Message} \n {exception.StackTrace}",
					Category.Lighting);
			}
		}

		private void TryAddBulb(HandApply interaction)
		{
			if (mState != LightMountState.MissingBulb) return;

			if (Validations.HasItemTrait(interaction.HandObject, CommonTraits.Instance.Broken))
			{
				ServerChangeLightState(LightMountState.Broken);
			}
			else
			{
				ServerChangeLightState(
					(switchState && (powerState == PowerState.On))
						? LightMountState.On
						: (powerState != PowerState.OverVoltage)
							? LightMountState.Emergency
							: LightMountState.Off);
			}

			_ = Despawn.ServerSingle(interaction.HandObject);
		}

		private void TryReplaceBulb(HandApply interaction)
		{
			if (mState != LightMountState.MissingBulb)
			{
				Spawn.ServerPrefab(itemInMount, interaction.Performer.WorldPosServer());
				ServerChangeLightState(LightMountState.MissingBulb);
			}
		}

		#endregion

		#region IAPCPowerable

		public void PowerNetworkUpdate(float voltage)
		{
		}

		public void StateUpdate(PowerState newPowerState)
		{
			if (isServer == false) return;
			powerState = newPowerState;
			if (mState == LightMountState.Broken
			    || mState == LightMountState.MissingBulb) return;

			switch (newPowerState)
			{
				case PowerState.On:
					ServerChangeLightState(LightMountState.On);
					return;
				case PowerState.LowVoltage:
					ServerChangeLightState(LightMountState.Emergency);
					return;
				case PowerState.OverVoltage:
					ServerChangeLightState(LightMountState.BurnedOut);
					return;
				case PowerState.Off:
					ServerChangeLightState(LightMountState.Emergency);
					return;
			}
		}

		#endregion

		#region SwitchRelatedLogic

		public void SubscribeToSwitchEvent(LightSwitchV2 lightSwitch)
		{
			UnSubscribeFromSwitchEvent();
			relatedLightSwitch = lightSwitch;
			lightSwitch.SwitchTriggerEvent += Trigger;
		}

		public void UnSubscribeFromSwitchEvent()
		{
			if (relatedLightSwitch == null) return;
			relatedLightSwitch.SwitchTriggerEvent -= Trigger;
			relatedLightSwitch = null;
		}

		public override void Trigger(bool newState)
		{
			if (isServer == false) return;
			switchState = newState;
			if (mState == LightMountState.On || mState == LightMountState.Off)
				ServerChangeLightState(newState ? LightMountState.On : LightMountState.Off);
		}

		#endregion

		#region Spark

		private void TrySpark()
		{
			//Has to be broken and have power to spark
			if (mState != LightMountState.Broken || powerState == PowerState.Off) return;

			InternalSpark(30f);
		}

		private void InternalSpark(float chanceToSpark)
		{
			//Clamp just in case
			chanceToSpark = Mathf.Clamp(chanceToSpark, 1, 100);

			//E.g will have 25% chance to not spark when chanceToSpark = 75
			if(DMMath.Prob(100 - chanceToSpark)) return;

			//Try start fire if possible
			var reactionManager = objectBehaviour.registerTile.Matrix.ReactionManager;
			reactionManager.ExposeHotspot(objectBehaviour.registerTile.LocalPositionServer, 1000);

			SoundManager.PlayNetworkedAtPos(CommonSounds.Instance.Sparks,
				objectBehaviour.registerTile.WorldPositionServer,
				sourceObj: gameObject);

			if (CustomNetworkManager.IsHeadless == false)
			{
				sparkObject.SetActive(true);
			}

			ClientRpcSpark();
		}

		[ClientRpc]
		private void ClientRpcSpark()
		{
			sparkObject.SetActive(true);
		}

		#endregion

		private void OnDamageReceived(DamageInfo arg0)
		{
			if (CustomNetworkManager.IsServer == false) return;

			CheckIntegrityState(arg0);
		}

		private void CheckIntegrityState(DamageInfo arg0)
		{
			if (integrity.integrity > integrityThreshBar || mState == LightMountState.MissingBulb) return;
			Vector3 pos = gameObject.AssumedWorldPosServer();

			if (mState == LightMountState.Broken)
			{
				ServerChangeLightState(LightMountState.MissingBulb);
				SoundManager.PlayNetworkedAtPos(CommonSounds.Instance.GlassStep, pos, sourceObj: gameObject);
			}
			else
			{
				ServerChangeLightState(LightMountState.Broken);
				Spawn.ServerPrefab(CommonPrefabs.Instance.GlassShard, pos, count: Random.Range(0, 2),
					scatterRadius: Random.Range(0, 2));
				//Because this can get destroyed by fire then it tries accessing the tile safe loop and Complaints
				if (arg0.AttackType != AttackType.Fire)
				{
					TrySpark();
				}
			}
		}
	}
}