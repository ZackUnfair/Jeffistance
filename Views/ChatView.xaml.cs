using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Jeffistance.Views
{
    public class ChatView : UserControl
    {
        
        public ChatView()
        {
            this.InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}