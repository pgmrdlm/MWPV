using System;

namespace MWPV.View.UserControls.CategoryItems
{
    public partial class CategoryItemAccountsPanel
    {
        public void ShowPersistenceError(string message)
        {
            ShowAccountError(message);
            SetErrors(true);
            UpdateTabButtons();
        }
    }
}
