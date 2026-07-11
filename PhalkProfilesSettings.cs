using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

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

        private bool isSyncing;
        public bool IsSyncing { get => isSyncing; set => SetValue(ref isSyncing, value); }

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
            // Save agora só persiste as configurações. A sincronização é
            // disparada manualmente pelo botão "Sync Now" (na própria view)
            // ou pelo item de menu Extensions -> Phalk Profiles -> Sync Now.
            plugin.SavePluginSettings(Settings);
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();

            if (string.IsNullOrWhiteSpace(Settings.ApiUrl))
            {
                errors.Add("Please enter the API URL.");
            }
            else if (!Uri.TryCreate(Settings.ApiUrl, UriKind.Absolute, out _))
            {
                errors.Add("The API URL is not valid.");
            }

            if (string.IsNullOrWhiteSpace(Settings.Username))
            {
                errors.Add("Please enter a username.");
            }

            if (string.IsNullOrWhiteSpace(Settings.Password))
            {
                errors.Add("Please enter a password.");
            }

            return errors.Count == 0;
        }

        /// <summary>
        /// Dispara uma sincronização completa da biblioteca sob demanda.
        /// Usado pelo botão "Sync Now" na tela de configurações.
        /// Retorna true se o servidor aceitou o lote.
        /// </summary>
        public async Task<bool> SyncNowAsync()
        {
            if (IsSyncing)
            {
                return false;
            }

            IsSyncing = true;
            try
            {
                return await plugin.SincronizarBibliotecaCompleta();
            }
            finally
            {
                IsSyncing = false;
            }
        }
    }
}
