using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Uno.Toolkit.UI;

namespace Alua
{
    public class AsyncCommand : ICommand, ILoadable
    {
        public event EventHandler CanExecuteChanged;
        public event EventHandler IsExecutingChanged;

        private readonly Func<Task> _executeAsync;
        private bool _isExecuting;

        public AsyncCommand(Func<Task> executeAsync)
        {
            _executeAsync = executeAsync;
        }

        public bool CanExecute(object parameter) => !IsExecuting;

        public bool IsExecuting
        {
            get => _isExecuting;
            private set
            {
                if (_isExecuting != value)
                {
                    _isExecuting = value;
                    IsExecutingChanged?.Invoke(this, EventArgs.Empty);
                    CanExecuteChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public async void Execute(object parameter)
        {
            try
            {
                IsExecuting = true;
                await _executeAsync();
            }
            finally
            {
                IsExecuting = false;
            }
        }
    }
}
