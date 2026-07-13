using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityScreenNavigator.Runtime.Core.Modal;

namespace ThanhDV.SaveKeeper.Example
{
    public class ConfirmModal : Modal
    {
        [SerializeField] private TMP_Text _title;
        [SerializeField] private TMP_Text _message;
        [SerializeField] private Button _btnConfirm;
        [SerializeField] private Button _btnCancel;

        private Action _onConfirm;
        private Action _onCancel;

        public void Setup(string title, string message, Action onConfirm, Action onCancel = null)
        {
            if (_title != null) _title.text = title;
            if (_message != null) _message.text = message;
            _onConfirm = onConfirm;
            _onCancel = onCancel;
        }

        private void OnEnable()
        {
            _btnConfirm.onClick.AddListener(OnConfirmClicked);
            _btnCancel.onClick.AddListener(OnCancelClicked);
        }

        private void OnDisable()
        {
            _btnConfirm.onClick.RemoveListener(OnConfirmClicked);
            _btnCancel.onClick.RemoveListener(OnCancelClicked);
        }

        private void OnConfirmClicked()
        {
            Action cb = _onConfirm;
            _onConfirm = null;
            _onCancel = null;
            UIManager.Instance.ModalContainer.Pop(true);
            cb?.Invoke();
        }

        private void OnCancelClicked()
        {
            Action cb = _onCancel;
            _onConfirm = null;
            _onCancel = null;
            UIManager.Instance.ModalContainer.Pop(true);
            cb?.Invoke();
        }
    }
}
