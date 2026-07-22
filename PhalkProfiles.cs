using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
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

        // Jogos com menos que isso de tempo jogado (em segundos) não são
        // enviados para o servidor - Game.Playtime é medido em segundos.
        private const ulong PlaytimeMinimoSegundos = 120;

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

        // Ao iniciar um jogo: Envia apenas este jogo individualmente com IsPlaying = 1
        public override void OnGameStarted(OnGameStartedEventArgs args)
        {
            var settings = SettingsViewModel.Settings;

            if (!SettingsValidas(settings))
            {
                logger.Warn("Phalk Profiles: Configurações ausentes. Dados do jogo não enviados.");
                return;
            }

            logger.Info($"Phalk Profiles: Jogo {args.Game.Name} iniciado, enviando atualização individual (IsPlaying = 1).");
            // Workaround: usamos DateTime.Now aqui em vez de args.Game.LastActivity, pois o valor
            // interno do Playnite pode não estar atualizado no exato instante deste evento.
            var payload = MontarPayload(args.Game, isPlaying: true, lastActivityOverride: DateTime.Now);
            Task.Run(() => EnviarParaServidor(payload, settings));
        }

        // Ao fechar um jogo: Envia apenas este jogo individualmente com IsPlaying = 0
        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            var settings = SettingsViewModel.Settings;

            if (!SettingsValidas(settings))
            {
                logger.Warn("Phalk Profiles: Configurações ausentes. Dados do jogo não enviados.");
                return;
            }

            if (args.Game.Playtime < PlaytimeMinimoSegundos)
            {
                logger.Info($"Phalk Profiles: Jogo {args.Game.Name} fechado com apenas {args.Game.Playtime}s jogados, abaixo do mínimo de {PlaytimeMinimoSegundos}s - não enviado.");
                return;
            }

            logger.Info($"Phalk Profiles: Jogo {args.Game.Name} fechado, enviando atualização individual (IsPlaying = 0).");
            var payload = MontarPayload(args.Game, isPlaying: false);
            Task.Run(() => EnviarParaServidor(payload, settings));
        }

        // Item de menu: Main Menu -> Extensions -> Phalk Profiles -> Sync Now
        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            yield return new MainMenuItem
            {
                Description = "Sync Now",
                MenuSection = "@Phalk Profiles",
                Action = (mainMenuItem) =>
                {
                    var settings = SettingsViewModel.Settings;

                    if (!SettingsValidas(settings))
                    {
                        PlayniteApi.Dialogs.ShowErrorMessage(
                            "Phalk Profiles is not configured yet. Please set the API URL, username and password in the extension settings first.",
                            "Phalk Profiles");
                        return;
                    }

                    PlayniteApi.Notifications.Add(new NotificationMessage(
                        "phalkprofiles-sync-start",
                        "Phalk Profiles: library sync started...",
                        NotificationType.Info));

                    Task.Run(async () =>
                    {
                        var success = await SincronizarBibliotecaCompleta();

                        PlayniteApi.Notifications.Add(new NotificationMessage(
                            "phalkprofiles-sync-done",
                            success
                                ? "Phalk Profiles: library sync completed successfully."
                                : "Phalk Profiles: library sync failed. Check Playnite's extension log for details.",
                            success ? NotificationType.Info : NotificationType.Error));
                    });
                }
            };
        }

        // Item de menu de contexto: Clique direito no jogo -> Phalk Profiles -> Sync Game Data
        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            yield return new GameMenuItem
            {
                Description = "Sync Game",
                MenuSection = "Phalk Profiles",
                Action = (gameMenuItem) =>
                {
                    var settings = SettingsViewModel.Settings;

                    // 1. Valida as configurações da extensão
                    if (!SettingsValidas(settings))
                    {
                        PlayniteApi.Dialogs.ShowErrorMessage(
                            "Phalk Profiles is not configured yet. Please set the API URL, username and password in the extension settings first.",
                            "Phalk Profiles");
                        return;
                    }

                    // 2. Verifica se há algum jogo selecionado
                    if (args.Games == null || !args.Games.Any())
                    {
                        return;
                    }

                    // 3. Processa em segundo plano para não travar a interface do Playnite
                    Task.Run(() =>
                    {
                        try
                        {
                            // Percorre todos os jogos selecionados (caso o usuário selecione mais de um de uma vez)
                            foreach (var game in args.Games)
                            {
                                logger.Info($"Phalk Profiles: Enviando atualização manual via menu para o jogo: {game.Name}");
                                var payload = MontarPayload(game);
                                EnviarParaServidor(payload, settings);
                            }

                            // Notifica sucesso
                            PlayniteApi.Notifications.Add(new NotificationMessage(
                                "phalkprofiles-game-sync-done",
                                "Phalk Profiles: Selected game(s) synchronized successfully.",
                                NotificationType.Info));
                        }
                        catch (Exception ex)
                        {
                            logger.Error(ex, "Erro ao sincronizar jogo(s) selecionado(s) manualmente.");

                            PlayniteApi.Notifications.Add(new NotificationMessage(
                                "phalkprofiles-game-sync-done",
                                "Phalk Profiles: Failed to sync selected game(s). Check logs for details.",
                                NotificationType.Error));
                        }
                    });
                }
            };
        }

        /// <summary>
        /// Varre toda a biblioteca do Playnite, monta um único array e envia em lote.
        /// Retorna true se o servidor aceitou o lote.
        /// </summary>
        public async Task<bool> SincronizarBibliotecaCompleta()
        {
            var settings = SettingsViewModel.Settings;

            if (!SettingsValidas(settings))
            {
                logger.Warn("Phalk Profiles: Varredura em lote cancelada - configurações ausentes (URL/Usuário/Senha).");
                return false;
            }

            var jogos = PlayniteApi.Database.Games
                .Where(game => game.Playtime >= PlaytimeMinimoSegundos)
                .ToList();
            logger.Info($"Phalk Profiles: Montando lote completo com {jogos.Count} jogo(s) (ignorados os com menos de {PlaytimeMinimoSegundos}s jogados)...");

            var loteJogos = new List<Dictionary<string, object>>();

            foreach (var game in jogos)
            {
                loteJogos.Add(MontarPayload(game));
            }

            return await EnviarLoteParaServidor(loteJogos, settings);
        }

        private bool SettingsValidas(PhalkProfilesSettings settings)
        {
            return !string.IsNullOrEmpty(settings.ApiUrl) &&
                   !string.IsNullOrEmpty(settings.Username) &&
                   !string.IsNullOrEmpty(settings.Password);
        }

        // isPlaying == true  -> jogo acabou de ser iniciado (IsPlaying = 1)
        // isPlaying == false -> jogo acabou de ser encerrado (IsPlaying = 0)
        // isPlaying == null  -> não inclui o campo IsPlaying (usado no envio em lote/sync manual)
        //
        // lastActivityOverride -> se informado, usa esse horário no campo "lastActivity" em vez de
        // game.LastActivity. Usado no início do jogo (OnGameStarted), já que o valor de
        // game.LastActivity mantido internamente pelo Playnite nem sempre está atualizado no exato
        // instante em que esse evento dispara. O valor é convertido para UTC antes do envio,
        // mantendo o formato esperado pela API: yyyy-MM-dd HH:mm:ss.
        private Dictionary<string, object> MontarPayload(Game game, bool? isPlaying = null, DateTime? lastActivityOverride = null)
        {
            var achievements = GetGameAchievements(game);
            var lastActivity = lastActivityOverride ?? game.LastActivity;

            var payload = new Dictionary<string, object>
            {
                { "id", game.Id.ToString() },
                { "name", game.Name },
                { "playTime", game.Playtime },
                { "lastActivity", FormatarDataUtc(lastActivity) },
                { "platform", game.Platforms != null && game.Platforms.Count > 0 ? game.Platforms[0].Name : "Desconhecida" },
                { "rating", game.UserScore },
                { "achievementsTotal", achievements.TotalCount },
                { "achievements", achievements.Unlocked }
            };

            if (isPlaying.HasValue)
            {
                payload["IsPlaying"] = isPlaying.Value ? 1 : 0;
            }

            return payload;
        }

        private static string FormatarDataUtc(DateTime? data)
        {
            return data?.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss");
        }

        private PlayniteAchievementsReader.AchievementsSummary GetGameAchievements(Game game)
        {
            var extensionsDataRoot = Path.GetDirectoryName(GetPluginUserDataPath());
            return PlayniteAchievementsReader.GetAchievementsForGame(game, extensionsDataRoot);
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

        // Envio em lote (usado na inicialização e no Sync Now)
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

        public async Task<bool> TestarAutenticacaoAsync(PhalkProfilesSettings settings)
        {
            if (!SettingsValidas(settings))
            {
                return false;
            }

            // Um lote vazio valida endpoint, conexão e credenciais sem alterar jogos.
            return await EnviarLoteParaServidor(
                new List<Dictionary<string, object>>(),
                settings);
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
