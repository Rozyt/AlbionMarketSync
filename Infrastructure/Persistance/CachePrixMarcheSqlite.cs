using AlbionMarketSync.Domain.Interfaces;
using Microsoft.Data.Sqlite;

namespace AlbionMarketSync.Infrastructure.Persistance;

/// <summary>
/// Cache persistant SQLite en mode WAL (Module 4.2) implémentant ICachePrixMarcheLocal.
/// Garantit qu'aucune requête Google Sheets n'est émise si le prix d'une cellule n'a pas varié.
/// </summary>
public sealed class CachePrixMarcheSqlite : ICachePrixMarcheLocal
{
    private readonly SqliteConnection _connexionSqlite;
    private readonly Lock _verrouBaseDeDonnees = new();

    public CachePrixMarcheSqlite(string cheminFichierBaseSqlite)
    {
        string chaineConnexion = new SqliteConnectionStringBuilder
        {
            DataSource = cheminFichierBaseSqlite,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        _connexionSqlite = new SqliteConnection(chaineConnexion);
        _connexionSqlite.Open();
        InitialiserSchemaTables();
    }

    private void InitialiserSchemaTables()
    {
        lock (_verrouBaseDeDonnees)
        {
            using var commandeSql = _connexionSqlite.CreateCommand();
            commandeSql.CommandText = """
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = NORMAL;

                CREATE TABLE IF NOT EXISTS CellPriceCache (
                    CellAddress TEXT PRIMARY KEY,
                    ItemTypeId TEXT NOT NULL,
                    PriceSilver REAL NOT NULL,
                    City TEXT NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS MarketPriceHistory (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ItemTypeId TEXT NOT NULL,
                    Tier INTEGER NOT NULL,
                    Enchantment INTEGER NOT NULL,
                    Quality INTEGER NOT NULL,
                    AuctionType TEXT NOT NULL,
                    MinPriceSilver REAL NOT NULL,
                    WeightedAvgSilver REAL NOT NULL,
                    TotalAmount INTEGER NOT NULL,
                    City TEXT NOT NULL,
                    TargetCell TEXT,
                    CapturedAtUtc TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS IX_MarketPriceHistory_Item
                ON MarketPriceHistory(ItemTypeId, Tier, Enchantment, CapturedAtUtc DESC);
                """;
            commandeSql.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Compare le nouveau prix observé avec la dernière valeur enregistrée dans SQLite pour cette cellule.
    /// Retourne true uniquement si l'écart est d'au moins 0,5 Argent (évite les requêtes réseau inutiles).
    /// </summary>
    public bool LePrixDeLaCelluleAChange(string coordonneeCellule, double nouveauPrixArgent, out double? ancienPrixEnCache)
    {
        ancienPrixEnCache = null;
        lock (_verrouBaseDeDonnees)
        {
            using var commandeSql = _connexionSqlite.CreateCommand();
            commandeSql.CommandText = "SELECT PriceSilver FROM CellPriceCache WHERE CellAddress = $cellule LIMIT 1;";
            commandeSql.Parameters.AddWithValue("$cellule", coordonneeCellule);

            object? resultat = commandeSql.ExecuteScalar();
            if (resultat is null or DBNull)
                return true;

            double ancienneValeur = Convert.ToDouble(resultat);
            ancienPrixEnCache = ancienneValeur;
            return Math.Abs(ancienneValeur - nouveauPrixArgent) >= 0.5;
        }
    }

    public void EnregistrerPrixCelluleSynchronise(string coordonneeCellule, string identifiantObjet, double prixArgent, string villeMarche)
    {
        lock (_verrouBaseDeDonnees)
        {
            using var commandeSql = _connexionSqlite.CreateCommand();
            commandeSql.CommandText = """
                INSERT INTO CellPriceCache (CellAddress, ItemTypeId, PriceSilver, City, UpdatedAtUtc)
                VALUES ($cellule, $objet, $prix, $ville, $dateUtc)
                ON CONFLICT(CellAddress) DO UPDATE SET
                    ItemTypeId = excluded.ItemTypeId,
                    PriceSilver = excluded.PriceSilver,
                    City = excluded.City,
                    UpdatedAtUtc = excluded.UpdatedAtUtc;
                """;
            commandeSql.Parameters.AddWithValue("$cellule", coordonneeCellule);
            commandeSql.Parameters.AddWithValue("$objet", identifiantObjet);
            commandeSql.Parameters.AddWithValue("$prix", Math.Round(prixArgent));
            commandeSql.Parameters.AddWithValue("$ville", villeMarche);
            commandeSql.Parameters.AddWithValue("$dateUtc", DateTime.UtcNow.ToString("O"));
            commandeSql.ExecuteNonQuery();
        }
    }

    public void EnregistrerHistoriqueMarche(
        string identifiantObjet,
        int palierTier,
        int niveauEnchantement,
        int niveauQualite,
        string typeEnchere,
        double prixMinimumArgent,
        double prixMoyenPondereArgent,
        int quantiteTotale,
        string villeMarche,
        string? coordonneeCelluleCible)
    {
        lock (_verrouBaseDeDonnees)
        {
            using var commandeSql = _connexionSqlite.CreateCommand();
            commandeSql.CommandText = """
                INSERT INTO MarketPriceHistory (
                    ItemTypeId, Tier, Enchantment, Quality, AuctionType,
                    MinPriceSilver, WeightedAvgSilver, TotalAmount, City, TargetCell, CapturedAtUtc
                ) VALUES (
                    $objet, $palier, $enchantement, $qualite, $typeEnchere,
                    $prixMin, $prixMoyen, $quantite, $ville, $cellule, $dateUtc
                );
                """;
            commandeSql.Parameters.AddWithValue("$objet", identifiantObjet);
            commandeSql.Parameters.AddWithValue("$palier", palierTier);
            commandeSql.Parameters.AddWithValue("$enchantement", niveauEnchantement);
            commandeSql.Parameters.AddWithValue("$qualite", niveauQualite);
            commandeSql.Parameters.AddWithValue("$typeEnchere", typeEnchere);
            commandeSql.Parameters.AddWithValue("$prixMin", prixMinimumArgent);
            commandeSql.Parameters.AddWithValue("$prixMoyen", prixMoyenPondereArgent);
            commandeSql.Parameters.AddWithValue("$quantite", quantiteTotale);
            commandeSql.Parameters.AddWithValue("$ville", villeMarche);
            commandeSql.Parameters.AddWithValue("$cellule", (object?)coordonneeCelluleCible ?? DBNull.Value);
            commandeSql.Parameters.AddWithValue("$dateUtc", DateTime.UtcNow.ToString("O"));
            commandeSql.ExecuteNonQuery();
        }
    }

    public int ObtenirNombreCellulesEnCache()
    {
        lock (_verrouBaseDeDonnees)
        {
            using var commandeSql = _connexionSqlite.CreateCommand();
            commandeSql.CommandText = "SELECT COUNT(*) FROM CellPriceCache;";
            return Convert.ToInt32(commandeSql.ExecuteScalar());
        }
    }

    /// <summary>
    /// Synchronise le cache SQLite avec l'état réel de la feuille 'Indice prix' :
    /// si l'utilisateur a remis une cellule à 0 ou l'a vidée dans Google Sheets, elle est purgée du cache SQLite
    /// pour garantir qu'elle sera réécrite au prochain passage à l'Hôtel des Ventes.
    /// </summary>
    public void SynchroniserCacheAvecFeuilleIndicePrix(IReadOnlyList<IReadOnlyList<string>> grilleIndicePrix)
    {
        if (grilleIndicePrix.Count == 0)
            return;

        lock (_verrouBaseDeDonnees)
        {
            var cellulesEnCache = new List<string>();
            using (var cmdLecture = _connexionSqlite.CreateCommand())
            {
                cmdLecture.CommandText = "SELECT CellAddress FROM CellPriceCache WHERE CellAddress LIKE '''Indice prix''!%';";
                using var lecteur = cmdLecture.ExecuteReader();
                while (lecteur.Read())
                {
                    cellulesEnCache.Add(lecteur.GetString(0));
                }
            }

            foreach (string cellule in cellulesEnCache)
            {
                double prixDansSheet = Domain.ServicesMetier.PlanificateurRequetesCraftCape.LirePrixNumeriqueDansGrilleIndicePrix(
                    grilleIndicePrix,
                    cellule);

                if (prixDansSheet <= 0)
                {
                    using var cmdSuppression = _connexionSqlite.CreateCommand();
                    cmdSuppression.CommandText = "DELETE FROM CellPriceCache WHERE CellAddress = $cellule;";
                    cmdSuppression.Parameters.AddWithValue("$cellule", cellule);
                    cmdSuppression.ExecuteNonQuery();
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_verrouBaseDeDonnees)
        {
            _connexionSqlite.Close();
            _connexionSqlite.Dispose();
        }
    }
}

