using ThanhDV.SaveKeeper.Singleton;
using ThanhDV.Utilities;

namespace ThanhDV.SaveKeeper.Example
{
    public class GameManager : PersistentMonoSingleton<GameManager>
    {
        protected override void Awake()
        {
            base.Awake();
            InitializeSaveKeeper();
        }

        private void InitializeSaveKeeper()
        {
            if (!SKSingleton.Exists)
            {
                SKSingleton.InitializeFull("MasterKey");
            }
        }
    }
}
