using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Playnite.SDK.Data;

namespace PhalkProfiles
{
    public class PhalkProfilesPlugin : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private static readonly HttpClient httpClient = new HttpClient();

        public PhalkProfilesSettingsViewModel SettingsViewModel { get; set; }
        public override Guid Id { get; } = Guid.Parse("a1b2c3d4-e5f6-7a8b-9c0d-1e2f3a4b5c6d");

        public PhalkProfilesPlugin(IPlayniteAPI api) : base(api)
        {
            Properties = new GenericPluginProperties
            {
                HasSettings = true
            };

            SettingsViewModel = new PhalkProfilesSettingsViewModel(this);
        }

        // Ao iniciar o Playnite: Envia a biblioteca inteira em lote
        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            logger.Info("Phalk Profiles: Playnite iniciado, disparando sincronização em lote da biblioteca.");
            Task.Run(() => SincronizarBibliotecaCompleta());
        }

        // Ao fechar um jogo: Envia apenas este jogo individualmente
        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            var settings = SettingsViewModel.Settings;

            if (!SettingsValidas(settings))
            {
                logger.Warn("Phalk Profiles: Configurações ausentes. Dados do jogo não enviados.");
                return;
            }

            logger.Info($"Phalk Profiles: Jogo {args.Game.Name} fechado, enviando atualização individual.");
            var payload = MontarPayload(args.Game);
            Task.Run(() => EnviarParaServidor(payload, settings));
        }

        /// <summary>
        /// Varre toda a biblioteca do Playnite, monta um único array e envia em lote.
        /// </summary>
        public async Task SincronizarBibliotecaCompleta()
        {
            var settings = SettingsViewModel.Settings;

            if (!SettingsValidas(settings))
            {
                logger.Warn("Phalk Profiles: Varredura em lote cancelada - configurações ausentes (URL/Usuário/Senha).");
                return;
            }

            var jogos = PlayniteApi.Database.Games.ToList();
            logger.Info($"Phalk Profiles: Montando lote completo com {jogos.Count} jogo(s)...");

            var loteJogos = new List<Dictionary<string, object>>();

            foreach (var game in jogos)
            {
                loteJogos.Add(MontarPayload(game));
            }

            await EnviarLoteParaServidor(loteJogos, settings);
        }

        private bool SettingsValidas(PhalkProfilesSettings settings)
        {
            return !string.IsNullOrEmpty(settings.ApiUrl) &&
                   !string.IsNullOrEmpty(settings.Username) &&
                   !string.IsNullOrEmpty(settings.Password);
        }

        private Dictionary<string, object> MontarPayload(Game game)
        {
            return new Dictionary<string, object>
            {
                { "id", game.Id.ToString() },
                { "name", game.Name },
                { "playTime", game.Playtime },
                { "lastActivity", game.LastActivity?.ToString("yyyy-MM-dd HH:mm:ss") },
                { "platform", game.Platforms != null && game.Platforms.Count > 0 ? game.Platforms[0].Name : "Desconhecida" },
                { "rating", game.UserScore },
                { "achievements", GetGameAchievements(game) }
            };
        }

        private List<Dictionary<string, object>> GetGameAchievements(Game game)
        {
            return new List<Dictionary<string, object>>();
        }

        // Envio individual (usado quando um único jogo é fechado)
        private async Task<bool> EnviarParaServidor(Dictionary<string, object> payload, PhalkProfilesSettings settings)
        {
            try
            {
                string json = Serialization.ToJson(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using (var request = new HttpRequestMessage(HttpMethod.Post, settings.ApiUrl))
                {
                    request.Content = content;
                    var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.Username}:{settings.Password}"));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

                    HttpResponseMessage response = await httpClient.SendAsync(request);
                    string responseBody = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        logger.Info($"Phalk Profiles: Jogo individual sincronizado com sucesso. Resposta: {responseBody}");
                        return true;
                    }
                    else
                    {
                        logger.Error($"Phalk Profiles: Erro no servidor ao enviar jogo individual ({response.StatusCode}): {responseBody}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Phalk Profiles: Falha crítica de rede ao enviar jogo individual.");
                return false;
            }
        }

        // Envio em lote (usado na inicialização)
        private async Task<bool> EnviarLoteParaServidor(List<Dictionary<string, object>> loteJogos, PhalkProfilesSettings settings)
        {
            try
            {
                string jsonLote = Serialization.ToJson(loteJogos);
                var content = new StringContent(jsonLote, Encoding.UTF8, "application/json");

                using (var request = new HttpRequestMessage(HttpMethod.Post, settings.ApiUrl))
                {
                    request.Content = content;
                    var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.Username}:{settings.Password}"));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

                    HttpResponseMessage response = await httpClient.SendAsync(request);
                    string responseBody = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        logger.Info($"Phalk Profiles: Lote de {loteJogos.Count} jogos enviado com sucesso. Resposta: {responseBody}");
                        return true;
                    }
                    else
                    {
                        logger.Error($"Phalk Profiles: Servidor rejeitou o lote ({response.StatusCode}): {responseBody}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Phalk Profiles: Falha crítica de rede ao tentar enviar o lote para a API.");
                return false;
            }
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return SettingsViewModel;
        }

        public override System.Windows.Controls.UserControl GetSettingsView(bool firstRunSettings)
        {
            return new PhalkProfilesSettingsView();
        }
    }
}