using ThanhDV.SaveKeeper.Singleton;
using UnityEngine;
using UnityEngine.UI;
using UnityScreenNavigator.Runtime.Core.Page;

namespace ThanhDV.SaveKeeper.Example
{
    public class HomePage : Page
    {
        [Space]
        [SerializeField] private Button _btnNewGame;
        [SerializeField] private Button _btnContinue;
        [SerializeField] private Button _btnLoadGame;
        [SerializeField] private Button _btnExit;

        private string _mostRecenProfileId;

        private void OnEnable()
        {
            _btnNewGame.onClick.AddListener(OnBtnNewGameClicked);
            _btnContinue.onClick.AddListener(OnBtnContinueClicked);
            _btnLoadGame.onClick.AddListener(OnBtnLoadGameClicked);
            _btnExit.onClick.AddListener(OnBtnExitClicked);

            _mostRecenProfileId = SKSingleton.Instance.GetMostRecentProfileId();

            _btnContinue.gameObject.SetActive(!string.IsNullOrEmpty(_mostRecenProfileId));
            _btnLoadGame.gameObject.SetActive(!string.IsNullOrEmpty(_mostRecenProfileId));
        }

        private void OnBtnNewGameClicked()
        {
            UIManager.Instance.PageContainer.Push(PageAddr.SLOT, true, pageId: PageAddr.SLOT);
        }

        private void OnBtnContinueClicked()
        {

        }

        private void OnBtnLoadGameClicked()
        {

        }

        private void OnBtnExitClicked()
        {
            Application.Quit();
        }
    }
}
