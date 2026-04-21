using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace MediaFileManager.Desktop.UserControls;

public sealed partial class AnimatedLogo : UserControl
{
    public AnimatedLogo()
    {
        InitializeComponent();
        Loaded += AnimatedLogo_Loaded;
    }

    private void AnimatedLogo_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        (Resources["GearsRotatingStoryboard"] as Storyboard)?.Begin();
    }
}
