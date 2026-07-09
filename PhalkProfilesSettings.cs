using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;

namespace PhalkProfiles
{
    public class PhalkProfilesSettings : ObservableObject
    {
        private string apiUrl = "https://www.phalk.net/profiles/api.php";
        private string username = "";
        private string password = "";

        public string ApiUrl { get => apiUrl; set => SetValue(ref apiUrl, value); }
        public string Username { get => username; set => SetValue(ref username, value); }

        // A senha fica salva em texto puro no arquivo de settings do plugin
        // (%AppData%\Playnite\ExtensionsData\<id>\config.json), mas trafega
        // via HTTPS + Basic Auth e é validada no servidor com sha1().
        public string Password { get => password; set => SetValue(ref password, value); }
    }

    public class PhalkProfilesSettingsViewModel : ObservableObject, ISettings
    {
        private readonly PhalkProfilesPlugin plugin;
        private PhalkProfilesSettings editingClone;

        public PhalkProfilesSettings Settings { get; set; }

        public PhalkProfilesSettingsViewModel(PhalkProfilesPlugin plugin)
        {
            this.plugin = plugin;
            var savedSettings = plugin.LoadPluginSettings<PhalkProfilesSettings>();
            Settings = savedSettings ?? new PhalkProfilesSettings();
        }

        public void BeginEdit()
        {
            editingClone = Serialization.GetClone(Settings);
        }

        public void CancelEdit()
        {
            Settings = editingClone;
        }

        public void EndEdit()
        {
            plugin.SavePluginSettings(Settings);

            // Toda vez que o usuário salva as configurações, dispara uma varredura
            // completa da biblioteca em segundo plano.
            System.Threading.Tasks.Task.Run(() => plugin.SincronizarBibliotecaCompleta());
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();

            if (string.IsNullOrWhiteSpace(Settings.ApiUrl))
            {
                errors.Add("Informe a URL da API.");
            }
            else if (!Uri.TryCreate(Settings.ApiUrl, UriKind.Absolute, out _))
            {
                errors.Add("A URL da API não é válida.");
            }

            if (string.IsNullOrWhiteSpace(Settings.Username))
            {
                errors.Add("Informe o usuário.");
            }

            if (string.IsNullOrWhiteSpace(Settings.Password))
            {
                errors.Add("Informe a senha.");
            }

            return errors.Count == 0;
        }
    }
}