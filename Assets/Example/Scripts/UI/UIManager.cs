using ThanhDV.Utilities;
using UnityEngine;
using UnityScreenNavigator.Runtime.Core.Modal;
using UnityScreenNavigator.Runtime.Core.Page;

namespace ThanhDV.SaveKeeper.Example
{
    public class UIManager : PersistentMonoSingleton<UIManager>
    {
        [Space]
        [SerializeField] private PageContainer _pageContainer; public PageContainer PageContainer => _pageContainer;
        [SerializeField] private ModalContainer _modalContainer; public ModalContainer ModalContainer => _modalContainer;

        private void Start()
        {
            _pageContainer.Push(PageAddr.HOME, false, pageId: PageAddr.HOME);
        }
    }
}
