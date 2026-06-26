using System;

namespace MWPV.View.UserControls.CategoryItems
{
    public partial class CategoryItemSecurityQuestionsPanel
    {
        public void ShowPersistenceError(string message)
        {
            ShowSecurityQuestionError(message);
            SetErrors(true);
            UpdateTabButtons();
        }
    }
}
