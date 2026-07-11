using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace PhalkProfiles
{
    internal static class PlayniteAchievementsReader
    {
        private const string DatabaseFileName = "achievement_cache.db";
        private const string KnownGuidHint = "e6aad2c9-6e06-4d8d-ac55-ac3b252b5f7b";
        private static readonly ILogger logger = LogManager.GetLogger();
        private static string _cachedDbPath;
        private static bool _lookupDone;

        private static string FindDatabasePath(string extensionsDataRoot)
        {
            if (_lookupDone)
            {
                return _cachedDbPath;
            }

            _lookupDone = true;

            var hintPath = Path.Combine(extensionsDataRoot, KnownGuidHint, DatabaseFileName);
            if (File.Exists(hintPath))
            {
                _cachedDbPath = hintPath;
                return _cachedDbPath;
            }

            try
            {
                if (Directory.Exists(extensionsDataRoot))
                {
                    var found = Directory.EnumerateFiles(extensionsDataRoot, DatabaseFileName, SearchOption.AllDirectories)
                                          .FirstOrDefault();
                    if (found != null)
                    {
                        logger.Info($"Phalk Profiles: achievement_cache.db encontrado em '{found}'.");
                        _cachedDbPath = found;
                        return _cachedDbPath;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Phalk Profiles: falha ao varrer ExtensionsData procurando o PlayniteAchievements.");
            }

            _cachedDbPath = null;
            return null;
        }

        public static void ResetCache()
        {
            _lookupDone = false;
            _cachedDbPath = null;
        }

        public static List<Dictionary<string, object>> GetAchievementsForGame(Game game, string extensionsDataRoot)
        {
            var result = new List<Dictionary<string, object>>();

            var dbPath = FindDatabasePath(extensionsDataRoot);
            if (dbPath == null || !File.Exists(dbPath))
            {
                return result; // plugin não instalado ou ainda sem dados
            }

            // Read Only + imutable evita conflito de lock com o PlayniteAchievements
            // gravando no banco durante um refresh em segundo plano.
            var connectionString = $"Data Source={dbPath};Version=3;Read Only=True;";

            const string sql = @"
                SELECT
                    g.ProviderKey,
                    ad.DisplayName,
                    ad.Description,
                    ad.Category,
                    ad.CategoryType,
                    ad.TrophyType,
                    ad.Points,
                    ad.GlobalPercentUnlocked,
                    ad.Rarity,
                    ad.Hidden,
                    ua.Unlocked,
                    ua.UnlockTimeUtc,
                    ua.ProgressNum,
                    ua.ProgressDenom
                FROM Games g
                JOIN UserGameProgress ugp ON ugp.GameId = g.Id
                JOIN Users u ON u.Id = ugp.UserId
                JOIN UserAchievements ua ON ua.UserGameProgressId = ugp.Id
                JOIN AchievementDefinitions ad ON ad.Id = ua.AchievementDefinitionId
                WHERE g.PlayniteGameId = @gameId
                  AND u.IsCurrentUser = 1
                ORDER BY g.ProviderKey, ad.DisplayOrder";

            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    using (var command = new SQLiteCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@gameId", game.Id.ToString());

                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                result.Add(new Dictionary<string, object>
                                {
                                    { "provider", reader["ProviderKey"] as string },
                                    { "name", reader["DisplayName"] as string },
                                    { "description", reader["Description"] as string },
                                    { "category", reader["Category"] as string },
                                    { "categoryType", reader["CategoryType"] as string },
                                    { "trophyType", reader["TrophyType"] as string },
                                    { "points", reader["Points"] as long? },
                                    { "globalPercentUnlocked", reader["GlobalPercentUnlocked"] as double? },
                                    { "rarity", reader["Rarity"] as string },
                                    { "hidden", Convert.ToInt64(reader["Hidden"]) == 1 },
                                    { "unlocked", Convert.ToInt64(reader["Unlocked"]) == 1 },
                                    { "unlockTimeUtc", reader["UnlockTimeUtc"] as string },
                                    { "progressNum", reader["ProgressNum"] as long? },
                                    { "progressDenom", reader["ProgressDenom"] as long? }
                                });
                            }
                        }
                    }
                }
            }
            catch (SQLiteException ex)
            {
                // Banco pode estar momentaneamente locked durante um refresh
                // do PlayniteAchievements. Não derruba o sync do jogo por causa disso.
                logger.Warn(ex, $"Phalk Profiles: não foi possível ler achievements de '{game.Name}' (banco ocupado?).");
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Phalk Profiles: falha inesperada lendo achievements de '{game.Name}'.");
            }

            return result;
        }
    }
}
