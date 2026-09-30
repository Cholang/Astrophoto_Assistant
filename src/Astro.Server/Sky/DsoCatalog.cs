using System.Text.RegularExpressions;
using Astro.Core.Sky;
using Microsoft.Data.Sqlite;

namespace Astro.Server.Sky;

/// <summary>딥스카이 대상 하나. 크기는 긴 쪽 각크기(분).</summary>
public sealed record Dso(
    string Id,
    /// <summary>대표 이름 (M45, NGC 7000 …)</summary>
    string Name,
    /// <summary>영문 통칭 (Pleiades …). 없으면 null</summary>
    string? CommonName,
    IReadOnlyList<string> Designations,
    Equatorial Position,
    double? Magnitude,
    double? SizeArcmin,
    string Type,
    string Constellation);

/// <summary>
/// 대상 목록. N.I.N.A.가 설치하며 만드는 데이터베이스(NINA.sqlite)를 읽기 전용으로 쓴다.
/// 약 1만 7천 개(메시에·NGC·IC·칼드웰·Sh2 …), 좌표는 J2000(도), 크기는 초 단위로 들어 있다.
/// </summary>
public sealed class DsoCatalog
{
    private readonly string _connection;

    public DsoCatalog(string? path = null)
    {
        path ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NINA", "NINA.sqlite");
        Available = File.Exists(path);
        _connection = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString();
    }

    public bool Available { get; }

    private static readonly Dictionary<string, string> TypeNames = new()
    {
        ["GALXY"] = "은하", ["GX+DN"] = "은하", ["GALCL"] = "은하단",
        ["BRTNB"] = "성운", ["CL+NB"] = "성단과 성운", ["DRKNB"] = "암흑 성운",
        ["OPNCL"] = "산개 성단", ["GLOCL"] = "구상 성단", ["PLNNB"] = "행성상 성운",
        ["SNREM"] = "초신성 잔해", ["ASTER"] = "별무리",
    };

    /// <summary>이름으로 찾기: "M45", "m 45", "NGC7000", "IC 434", "Sh2-155", "C14", "Pleiades", "Orion Nebula"</summary>
    public IReadOnlyList<Dso> Find(string query, int limit = 5)
    {
        if (!Available || string.IsNullOrWhiteSpace(query)) return [];
        query = query.Trim();
        using var db = Open();

        var m = Regex.Match(query, @"^(M|NGC|IC|C|CALDWELL|SH\s*2|SH2|B|BARNARD)\s*-?\s*(\d+)$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var cat = m.Groups[1].Value.ToUpperInvariant().Replace(" ", "") switch
            {
                "C" or "CALDWELL" => "Caldwell",
                "SH2" => "Sh2",
                "B" or "BARNARD" => "Barnard",
                var c => c,
            };
            var ids = Ids(db, "select dsodetailid from cataloguenr where catalogue = $c and designation = $d", ("$c", cat), ("$d", m.Groups[2].Value));
            return Load(db, ids);
        }

        // 통칭: 정확히 같은 이름 먼저, 그다음 포함
        var exact = Ids(db, "select dsodetailid from cataloguenr where catalogue = 'NAME' and designation = $q collate nocase", ("$q", query));
        if (exact.Count > 0) return Load(db, exact.Take(limit));
        var like = Ids(db, "select distinct dsodetailid from cataloguenr where catalogue = 'NAME' and designation like $q collate nocase limit 20", ("$q", $"%{query}%"));
        return Load(db, like).OrderBy(d => d.Magnitude ?? 99).Take(limit).ToList();
    }

    public Dso? Get(string id)
    {
        if (!Available) return null;
        using var db = Open();
        return Load(db, [id]).FirstOrDefault();
    }

    /// <summary>추천 후보: 메시에·칼드웰 전체 (별·없는 대상 제외). 사진 대상으로 잘 알려진 것들</summary>
    public IReadOnlyList<Dso> Showpieces()
    {
        if (!Available) return [];
        using var db = Open();
        var ids = Ids(db, "select distinct dsodetailid from cataloguenr where catalogue in ('M', 'Caldwell')");
        return Load(db, ids).Where(d => d.Type != "기타").ToList();
    }

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(_connection);
        db.Open();
        return db;
    }

    private static List<string> Ids(SqliteConnection db, string sql, params (string Name, string Value)[] args)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    private static List<Dso> Load(SqliteConnection db, IEnumerable<string> ids)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0) return [];
        var names = new Dictionary<string, List<(string Cat, string Des)>>();
        var result = new List<Dso>();
        // 한 번에 불러온다 (후보 전체 ~220개)
        var inList = string.Join(",", idList.Select((_, i) => $"$p{i}"));
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = $"select dsodetailid, catalogue, designation from cataloguenr where dsodetailid in ({inList})";
            for (var i = 0; i < idList.Count; i++) cmd.Parameters.AddWithValue($"$p{i}", idList[i]);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (!names.TryGetValue(r.GetString(0), out var l)) names[r.GetString(0)] = l = [];
                l.Add((r.GetString(1), r.GetString(2)));
            }
        }
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = $"select id, ra, dec, magnitude, sizemax, dsotype, constellation from dsodetail where id in ({inList})";
            for (var i = 0; i < idList.Count; i++) cmd.Parameters.AddWithValue($"$p{i}", idList[i]);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var id = r.GetString(0);
                var n = names.GetValueOrDefault(id) ?? [];
                string? Pick(string cat, string prefix) => n.Where(x => x.Cat == cat).Select(x => prefix + x.Des).FirstOrDefault();
                var designations = new[] { Pick("M", "M"), Pick("NGC", "NGC "), Pick("IC", "IC "), Pick("Caldwell", "C"), Pick("Sh2", "Sh2-") }
                    .OfType<string>().ToList();
                var common = n.Where(x => x.Cat == "NAME").Select(x => x.Des).OrderBy(x => x.Length).FirstOrDefault();
                result.Add(new Dso(
                    id,
                    designations.FirstOrDefault() ?? id,
                    common,
                    designations,
                    new Equatorial(r.GetDouble(1), r.GetDouble(2)),
                    r.IsDBNull(3) ? null : r.GetDouble(3),
                    r.IsDBNull(4) ? null : r.GetDouble(4) / 60,
                    TypeNames.GetValueOrDefault(r.IsDBNull(5) ? "" : r.GetString(5), "기타"),
                    r.IsDBNull(6) ? "" : Constellations.Korean(r.GetString(6))));
            }
        }
        // 요청한 순서대로
        return idList.Select(id => result.FirstOrDefault(d => d.Id == id)).OfType<Dso>().ToList();
    }
}
