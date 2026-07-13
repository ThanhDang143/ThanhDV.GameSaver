using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ThanhDV.SaveKeeper.Example
{
    public static class PageAddr
    {
        public const string HOME = "HomePage";
        public const string SLOT = "SlotPage";
        public const string MAIN = "MainPage";
    }

    public static class ModalAddr
    {
        public const string CONFIRM = "ConfirmModal";
    }

    public static class SaveSlot
    {
        private static string[] _slotId = new string[] { "SaveSlot_1", "SaveSlot_2", "SaveSlot_3" };

        public static int GetSlotNumber()
        {
            if (_slotId == null) return 0;

            return _slotId.Length;
        }

        public static string GetSlotId(int index)
        {
            if (_slotId == null) return null;
            if (index < 0 || index >= _slotId.Length) return null;

            return _slotId[index];
        }
    }
}
