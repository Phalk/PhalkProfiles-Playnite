using System.Windows.Controls;

namespace PhalkProfiles
{
    // O namespace PRECISA ser exatamente PhalkProfiles
    public partial class PhalkProfilesSettingsView : UserControl
    {
        public PhalkProfilesSettingsView()
        {
            InitializeComponent();

            // DataContext só é definido pelo Playnite quando a view é aberta,
            // então esperamos o Loaded para popular o PasswordBox com o valor salvo.
            Loaded += (s, e) =>
            {
                if (DataContext is PhalkProfilesSettingsViewModel vm)
                {
                    PasswordInput.Password = vm.Settings.Password;
                }
            };
        }

        private void PasswordInput_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is PhalkProfilesSettingsViewModel vm)
            {
                vm.Settings.Password = PasswordInput.Password;
            }
        }
    }
}