using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Remoting.Channels;
using Systems.Atmospherics;
using Mirror;
using Objects.Atmospherics;
using UnityEditor.Build.Player;
using UnityEngine;

namespace Items.Devices
{
	public class TankTransferValve : NetworkBehaviour, ICheckedInteractable<HandApply>
	{
		[SerializeField] public Dictionary<string, Texture> prefabSpriteTable;

		[SerializeField] private SpriteHandler centerSprite;
		[SerializeField] private SpriteHandler leftTank;
		[SerializeField] private SpriteHandler rightTank;

		private GasMix tankMixOne;
		private GasMix tankMixTwo;

		private bool tankOnePresent;
		private bool tankTwoPresent;

		//private GasContainer tankOne;
		//private GasContainer tankTwo;

		public bool valveOpenState = false;

		public void OpenValve()
		{
			tankMixOne.MergeGasMix(tankMixTwo);
			valveOpenState = true;
		}

		public void CloseValve()
		{

			valveOpenState = false;
		}
		public bool WillInteract(HandApply interaction, NetworkSide side)
		{
			//start with the default HandApply WillInteract logic.
			if (!DefaultWillInteract.Default(interaction, side)) return false;



			return Validations.HasUsedItemTrait(interaction, CommonTraits.Instance.TransferableTank);
		}

		public void ServerPerformInteraction(HandApply interaction)
		{
			if (interaction.HandObject == null)
			{
				//Open tab for valve
			}
			else if (Validations.HasUsedItemTrait(interaction, CommonTraits.Instance.TransferableTank))
			{
				//Hide/store the tank item
				StoreTank();

				//Transfer gas from tanks

			}

		}

		public bool CanStore()
		{
			bool isStoreable = false;



			return isStoreable;
		}

		public void StoreTank()
		{

		}

		public void RetreiveTank()
		{

		}

		public void mergeGasses()
		{
			tankMixOne.MergeGasMix(tankMixTwo);
			Tank
		}

		public bool ReadyForDetonation()
		{
			return (tankOnePresent && tankTwoPresent);
		}
	}
}

