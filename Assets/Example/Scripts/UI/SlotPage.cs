using System.Collections.Generic;
using ThanhDV.SaveKeeper.Singleton;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityScreenNavigator.Runtime.Core.Page;

namespace ThanhDV.SaveKeeper.Example
{
    public class SlotPage : Page
    {
        [SerializeField] private RectTransform _content;
        [SerializeField] private Button _slotTemplate;

        private readonly List<GameObject> _slotItems = new();

        public override void DidPushEnter()
        {
            base.DidPushEnter();
            RebuildSlots();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                UIManager.Instance.PageContainer.Pop(true);
            }
        }

        private void RebuildSlots()
        {
            ClearSlots();
            _slotTemplate.gameObject.SetActive(false);

            int slotCount = SaveSlot.GetSlotNumber();
            for (int index = 0; index < slotCount; index++)
            {
                string slotId = SaveSlot.GetSlotId(index);
                Button button = Instantiate(_slotTemplate, _content);
                TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);

                button.name = $"Slot_{index + 1}";
                button.gameObject.SetActive(true);
                label.text = $"SLOT {index + 1}\n<size=55%><color=#8FA4C2>{slotId}</color></size>";
                button.onClick.AddListener(() => SelectSlot(slotId));

                _slotItems.Add(button.gameObject);
            }
        }

        private void ClearSlots()
        {
            foreach (GameObject item in _slotItems)
            {
                if (item != null) Destroy(item);
            }

            _slotItems.Clear();
        }

        private void SelectSlot(string slotId)
        {
            bool exists = SKSingleton.Instance.ProfileExists(slotId);
            if (exists)
            {
                UIManager.Instance.ModalContainer.Push<ConfirmModal>(
                    ModalAddr.CONFIRM,
                    true,
                    modalId: ModalAddr.CONFIRM,
                    onLoad: x => x.modal.Setup(
                        "Override Save Slot?",
                        $"Slot \"{slotId}\" already has a save. Do you want to override it?",
                        onConfirm: () => SKSingleton.Instance.CreateProfile(slotId, null, true)));
            }
            else
            {
                SKSingleton.Instance.CreateProfile(slotId, null, exists);
            }
        }
    }
}