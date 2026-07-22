using System;
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Navigation;

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

        private async void SaveButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (!(DataContext is PhalkProfilesSettingsViewModel vm))
            {
                return;
            }

            if (!vm.VerifySettings(out var errors))
            {
                SaveStatusText.Foreground = Brushes.OrangeRed;
                SaveStatusText.Text = string.Join(" ", errors);
                return;
            }

            SaveButton.IsEnabled = false;
            SaveStatusText.Foreground = Brushes.Gray;
            SaveStatusText.Text = "Authenticating...";

            try
            {
                var authenticated = await vm.AuthenticateAndSaveAsync();
                SaveStatusText.Foreground = authenticated ? Brushes.Green : Brushes.OrangeRed;
                SaveStatusText.Text = authenticated
                    ? "Settings saved."
                    : "Authentication failed. Check the API URL, username and password.";
            }
            finally
            {
                SaveButton.IsEnabled = true;
            }
        }

        private void ViewProfileButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is PhalkProfilesSettingsViewModel vm && vm.Settings.IsAuthenticated)
            {
                OpenUrl("https://phalk.net/profiles/profile?u=" +
                    Uri.EscapeDataString(vm.Settings.AuthenticatedUsername));
            }
        }

        private void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            OpenUrl(e.Uri.AbsoluteUri);
            e.Handled = true;
        }

        private static void OpenUrl(string url)
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        private async void SyncNowButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (!(DataContext is PhalkProfilesSettingsViewModel vm))
            {
                return;
            }

            // Valida antes de tentar sincronizar, pra dar um feedback claro
            // em vez de só falhar silenciosamente lá no fundo.
            if (!vm.VerifySettings(out var errors))
            {
                SyncStatusText.Foreground = Brushes.OrangeRed;
                SyncStatusText.Text = string.Join(" ", errors);
                return;
            }

            SyncNowButton.IsEnabled = false;
            SyncStatusText.Foreground = Brushes.Gray;
            SyncStatusText.Text = "Syncing your library, this may take a moment...";

            try
            {
                var success = await vm.SyncNowAsync();

                if (success)
                {
                    SyncStatusText.Foreground = Brushes.Green;
                    SyncStatusText.Text = "Library synced successfully.";
                }
                else
                {
                    SyncStatusText.Foreground = Brushes.OrangeRed;
                    SyncStatusText.Text = "Sync failed. Check Playnite's extension log for details.";
                }
            }
            finally
            {
                SyncNowButton.IsEnabled = true;
            }
        }
    }
}
