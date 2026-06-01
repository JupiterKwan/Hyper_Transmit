using CommunityToolkit.Mvvm.ComponentModel;

namespace Hyper_Transmit.ViewModels
{
    /// <summary>
    /// Base ViewModel with common properties for loading state and status messages.
    /// </summary>
    public abstract partial class BaseViewModel : ObservableObject
    {
        /// <summary>
        /// Indicates if the ViewModel is currently performing an operation.
        /// </summary>
        [ObservableProperty]
        private bool _isLoading;

        /// <summary>
        /// Status message to display to the user.
        /// </summary>
        [ObservableProperty]
        private string _statusMessage = "";

        /// <summary>
        /// Whether there is an error message to display.
        /// </summary>
        [ObservableProperty]
        private bool _hasError;

        /// <summary>
        /// Error message text.
        /// </summary>
        [ObservableProperty]
        private string _errorMessage = "";

        /// <summary>
        /// Sets an error message and marks HasError.
        /// </summary>
        protected void SetError(string message)
        {
            ErrorMessage = message;
            HasError = true;
        }

        /// <summary>
        /// Clears the error state.
        /// </summary>
        protected void ClearError()
        {
            ErrorMessage = "";
            HasError = false;
        }
    }
}