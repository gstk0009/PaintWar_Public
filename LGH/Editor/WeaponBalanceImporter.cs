using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

public class WeaponBalanceImporter : EditorWindow
{
    private const string DefaultExcelPath = "Assets/LGH/Balance/WeaponBalance.xlsx";
    private const string DefaultOutputPath = "Assets/LGH/Data/Weapons";
    private const int PatternStartRow = 14;
    private const int Level3PatternStartRow = 26;
    private const int Level5PatternStartRow = 38;
    private const int PatternStartColumn = 2;
    private const int PatternColumns = 12;
    private const int PatternRows = 9;

    private string excelPath = DefaultExcelPath;
    private string outputPath = DefaultOutputPath;
    private string googleSheetUrl = "https://docs.google.com/spreadsheets/d/1upymF0ASlYspsZMwppJfzB17Di0LQVFS/edit?usp=drive_link&ouid=102851924642518992631&rtpof=true&sd=true";
    private bool isDownloading;

    [MenuItem("LGH/Weapons/Balance Importer")]
    public static void Open()
    {
        GetWindow<WeaponBalanceImporter>("Weapon Balance");
    }

    [MenuItem("LGH/Weapons/Create Excel Template")]
    public static void CreateDefaultExcelTemplate()
    {
        WriteWorkbook(DefaultExcelPath, CreateDefaultRows());
        AssetDatabase.Refresh();
        Debug.Log("Created weapon Excel template: " + DefaultExcelPath);
    }

    [MenuItem("LGH/Weapons/Import Default Weapon Excel")]
    public static void ImportDefault()
    {
        if (!File.Exists(ToFullPath(DefaultExcelPath)))
        {
            CreateDefaultExcelTemplate();
        }

        Import(DefaultExcelPath, DefaultOutputPath);
    }

    [MenuItem("LGH/Weapons/Download Google Sheet And Import")]
    public static void DownloadDefaultGoogleSheetAndImport()
    {
        DownloadDefaultGoogleSheetAndImportAsync().Forget();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Weapon Excel Import", EditorStyles.boldLabel);
        excelPath = EditorGUILayout.TextField("Excel Path", excelPath);
        outputPath = EditorGUILayout.TextField("Output Path", outputPath);

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Google Sheets Sync", EditorStyles.boldLabel);
        googleSheetUrl = EditorGUILayout.TextField("Sheet URL or ID", googleSheetUrl);

        EditorGUI.BeginDisabledGroup(isDownloading);
        if (GUILayout.Button("Download Google Sheet To Excel"))
        {
            DownloadFromWindowAsync(false).Forget();
        }

        if (GUILayout.Button("Download Google Sheet And Import"))
        {
            DownloadFromWindowAsync(true).Forget();
        }
        EditorGUI.EndDisabledGroup();

        if (isDownloading)
        {
            EditorGUILayout.HelpBox("Downloading weapon balance xlsx...", MessageType.Info);
        }

        if (GUILayout.Button("Create Excel Template"))
        {
            CreateDefaultExcelTemplate();
        }

        if (GUILayout.Button("Import Excel To WeaponData"))
        {
            Import(excelPath, outputPath);
        }
    }

    private async UniTaskVoid DownloadFromWindowAsync(bool importAfterDownload)
    {
        isDownloading = true;
        Repaint();

        try
        {
            bool downloaded = await DownloadGoogleSheetAsync(googleSheetUrl, excelPath);
            if (downloaded && importAfterDownload)
            {
                Import(excelPath, outputPath);
            }
        }
        finally
        {
            isDownloading = false;
            Repaint();
        }
    }

    private static async UniTaskVoid DownloadDefaultGoogleSheetAndImportAsync()
    {
        if (await DownloadGoogleSheetAsync("https://docs.google.com/spreadsheets/d/1upymF0ASlYspsZMwppJfzB17Di0LQVFS/edit?usp=drive_link&ouid=102851924642518992631&rtpof=true&sd=true", DefaultExcelPath))
        {
            Import(DefaultExcelPath, DefaultOutputPath);
        }
    }

    private static async UniTask<bool> DownloadGoogleSheetAsync(string sheetUrlOrId, string targetExcelAssetPath)
    {
        string exportUrl = BuildDownloadUrl(sheetUrlOrId);
        if (string.IsNullOrWhiteSpace(exportUrl))
        {
            Debug.LogError("Google Sheet URL, Drive file URL, or spreadsheet ID is empty.");
            return false;
        }

        string targetFullPath = ToFullPath(targetExcelAssetPath);
        Directory.CreateDirectory(Path.GetDirectoryName(targetFullPath));

        using (UnityWebRequest request = UnityWebRequest.Get(exportUrl))
        {
            request.SetRequestHeader("Cache-Control", "no-cache");
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                EditorUtility.DisplayProgressBar("Downloading Google Sheet", exportUrl, request.downloadProgress);
                await UniTask.Yield();
            }

            EditorUtility.ClearProgressBar();
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Failed to download Google Sheet: " + request.error);
                return false;
            }

            byte[] data = request.downloadHandler.data;
            if (!IsXlsx(data))
            {
                Debug.LogError("Downloaded data was not an xlsx file. Check that the Google file is shared with link access and use the WeaponBalance.xlsx file link, not the folder link.");
                return false;
            }

            File.WriteAllBytes(targetFullPath, data);
        }

        AssetDatabase.Refresh();
        Debug.Log("Downloaded Google Sheet to " + targetExcelAssetPath);
        return true;
    }

    private static string BuildDownloadUrl(string sheetUrlOrId)
    {
        if (string.IsNullOrWhiteSpace(sheetUrlOrId))
        {
            return string.Empty;
        }

        string value = sheetUrlOrId.Trim();
        if (value.Contains("/folders/"))
        {
            Debug.LogError("Google Drive folder links cannot be downloaded as a single xlsx. Use the WeaponBalance.xlsx file link inside the folder.");
            return string.Empty;
        }

        string spreadsheetId = ExtractBetween(value, "/spreadsheets/d/");
        if (!string.IsNullOrWhiteSpace(spreadsheetId))
        {
            return "https://docs.google.com/spreadsheets/d/" + spreadsheetId + "/export?format=xlsx";
        }

        string driveFileId = ExtractBetween(value, "/file/d/");
        if (string.IsNullOrWhiteSpace(driveFileId))
        {
            driveFileId = ExtractQueryValue(value, "id");
        }

        if (!string.IsNullOrWhiteSpace(driveFileId))
        {
            return "https://drive.google.com/uc?export=download&id=" + driveFileId;
        }

        if (value.StartsWith("http://") || value.StartsWith("https://"))
        {
            return value;
        }

        return "https://docs.google.com/spreadsheets/d/" + value + "/export?format=xlsx";
    }

    private static string ExtractSpreadsheetId(string sheetUrlOrId)
    {
        if (string.IsNullOrWhiteSpace(sheetUrlOrId))
        {
            return string.Empty;
        }

        string value = sheetUrlOrId.Trim();
        const string marker = "/spreadsheets/d/";
        int markerIndex = value.IndexOf(marker, System.StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
        {
            int start = markerIndex + marker.Length;
            int end = value.IndexOf('/', start);
            return end >= 0 ? value.Substring(start, end - start) : value.Substring(start);
        }

        return value;
    }

    private static string ExtractBetween(string value, string marker)
    {
        int markerIndex = value.IndexOf(marker, System.StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return string.Empty;
        }

        int start = markerIndex + marker.Length;
        int end = value.IndexOfAny(new[] { '/', '?', '&', '#' }, start);
        return end >= 0 ? value.Substring(start, end - start) : value.Substring(start);
    }

    private static string ExtractQueryValue(string value, string key)
    {
        int queryIndex = value.IndexOf('?');
        if (queryIndex < 0)
        {
            return string.Empty;
        }

        string[] pairs = value.Substring(queryIndex + 1).Split('&');
        foreach (string pair in pairs)
        {
            string[] parts = pair.Split('=');
            if (parts.Length == 2 && parts[0] == key)
            {
                return System.Uri.UnescapeDataString(parts[1]);
            }
        }

        return string.Empty;
    }

    private static bool IsXlsx(byte[] data)
    {
        return data != null && data.Length > 4 && data[0] == 'P' && data[1] == 'K';
    }

    public static void Import(string excelAssetPath, string outputAssetPath)
    {
        string fullPath = ToFullPath(excelAssetPath);
        if (!File.Exists(fullPath))
        {
            Debug.LogError("Weapon Excel file not found: " + excelAssetPath);
            return;
        }

        if (!AssetDatabase.IsValidFolder(outputAssetPath))
        {
            Directory.CreateDirectory(outputAssetPath);
            AssetDatabase.Refresh();
        }

        int imported = 0;
        foreach (WeaponExcelRow row in ReadWorkbook(excelAssetPath))
        {
            if (string.IsNullOrWhiteSpace(row.Id))
            {
                continue;
            }

            string assetPath = outputAssetPath + "/" + row.Id + ".asset";
            WeaponData weapon = AssetDatabase.LoadAssetAtPath<WeaponData>(assetPath);
            if (weapon == null)
            {
                weapon = CreateInstance<WeaponData>();
                AssetDatabase.CreateAsset(weapon, assetPath);
            }

            weapon.SetData(
                row.Id,
                row.DisplayName,
                row.Category,
                row.PatternType,
                row.Cooldown,
                row.Range,
                row.DelaySeconds,
                row.Description,
                row.Pivot,
                row.PatternRows,
                row.Level3PatternRows != null ? row.Level3Pivot : (Vector2Int?)null,
                row.Level3PatternRows,
                row.Level5PatternRows != null ? row.Level5Pivot : (Vector2Int?)null,
                row.Level5PatternRows,
                null,
                row.StatusEffect,
                weapon.VfxProfile);
            weapon.SetSortOrder(row.SortOrder);
            EditorUtility.SetDirty(weapon);
            imported++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Imported " + imported + " weapons from " + excelAssetPath);
    }

    private static List<WeaponExcelRow> ReadWorkbook(string excelAssetPath)
    {
        string fullPath = ToFullPath(excelAssetPath);
        List<WeaponExcelRow> rows = new List<WeaponExcelRow>();

        using (FileStream file = File.Open(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (ZipArchive archive = new ZipArchive(file, ZipArchiveMode.Read))
        {
            XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            Dictionary<string, string> sharedStrings = ReadSharedStrings(archive);
            XDocument workbook = LoadXml(archive, "xl/workbook.xml");
            XDocument rels = LoadXml(archive, "xl/_rels/workbook.xml.rels");
            Dictionary<string, string> relationshipTargets = rels.Root.Elements()
                .ToDictionary(e => (string)e.Attribute("Id"), e => (string)e.Attribute("Target"));

            foreach (XElement sheet in workbook.Root.Element(main + "sheets").Elements(main + "sheet"))
            {
                string sheetName = (string)sheet.Attribute("name");
                if (string.IsNullOrWhiteSpace(sheetName) || sheetName.StartsWith("_"))
                {
                    continue;
                }

                string relationshipId = (string)sheet.Attribute(relNs + "id");
                if (!relationshipTargets.TryGetValue(relationshipId, out string target))
                {
                    continue;
                }

                Dictionary<string, string> cells = ReadWorksheetCells(archive, "xl/" + target.TrimStart('/'), sharedStrings);
                rows.Add(ReadWeaponSheet(sheetName, cells));
            }
        }

        return rows.OrderBy(row => row.SortOrder).ThenBy(row => row.Id).ToList();
    }

    private static WeaponExcelRow ReadWeaponSheet(string sheetName, Dictionary<string, string> cells)
    {
        Vector2Int pivot = new Vector2Int(ParseInt(GetCell(cells, "B9"), 2), ParseInt(GetCell(cells, "B10"), 2));
        WeaponExcelRow row = new WeaponExcelRow
        {
            Id = GetCell(cells, "B1", sheetName),
            SortOrder = ParseInt(GetCell(cells, "B2"), 0),
            DisplayName = GetCell(cells, "B3"),
            Category = GetCell(cells, "B4"),
            PatternType = ParsePattern(GetCell(cells, "B5")),
            Cooldown = ParseFloat(GetCell(cells, "B6"), 1f),
            Range = ParseInt(GetCell(cells, "B7"), 1),
            DelaySeconds = ParseFloat(GetCell(cells, "B8"), 0f),
            Pivot = pivot,
            Level3Pivot = pivot,
            Level5Pivot = pivot,
            Description = GetCell(cells, "B11"),
            Notes = GetCell(cells, "B12")
        };

        string effectTypeCell = GetCell(cells, "O1");
        row.StatusEffect = string.IsNullOrWhiteSpace(effectTypeCell)
            ? GetDefaultStatusEffect(row.Id)
            : new StatusEffectSpec(
                ParseEnum(effectTypeCell, StatusEffectType.None),
                ParseFloat(GetCell(cells, "O2"), 0.5f),
                ParseFloat(GetCell(cells, "O3"), 0f),
                ParseEnum(GetCell(cells, "O4"), StatusEffectStackMode.RefreshDuration));

        PatternBlock level1Pattern = ReadPatternBlock(cells, PatternStartRow, PatternColumns, PatternRows, pivot);
        if (level1Pattern != null)
        {
            row.Pivot = level1Pattern.Pivot;
            row.PatternRows = level1Pattern.Rows;
        }

        PatternBlock level3Pattern = ReadPatternBlock(cells, Level3PatternStartRow, PatternColumns, PatternRows, row.Pivot);
        if (level3Pattern != null)
        {
            row.Level3Pivot = level3Pattern.Pivot;
            row.Level3PatternRows = level3Pattern.Rows;
        }

        PatternBlock level5Pattern = ReadPatternBlock(cells, Level5PatternStartRow, PatternColumns, PatternRows, row.Level3Pivot);
        if (level5Pattern != null)
        {
            row.Level5Pivot = level5Pattern.Pivot;
            row.Level5PatternRows = level5Pattern.Rows;
        }

        return row;
    }

    private static PatternBlock ReadPatternBlock(Dictionary<string, string> cells, int startRow, int width, int height, Vector2Int fallbackPivot)
    {
        char[,] grid = new char[height, width];
        Vector2Int pivot = fallbackPivot;
        bool hasPaintCell = false;
        bool foundPivotMarker = false;

        // 각 레벨 블록 안의 2를 플레이어 위치로 사용하고, 없을 때만 전달받은 pivot을 씁니다.
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                string cellName = CellName(PatternStartColumn + x, startRow + y);
                char cell = NormalizePatternCell(GetCell(cells, cellName));
                grid[y, x] = cell;

                if (IsPaintCell(cell))
                {
                    hasPaintCell = true;
                }

                if (cell == '2')
                {
                    pivot = new Vector2Int(x, y);
                    foundPivotMarker = true;
                }
            }
        }

        if (!hasPaintCell)
        {
            return null;
        }

        return TrimPatternBlock(grid, width, height, foundPivotMarker ? pivot : fallbackPivot);
    }

    private static PatternBlock TrimPatternBlock(char[,] grid, int width, int height, Vector2Int pivot)
    {
        int minX = width;
        int maxX = -1;
        int minY = height;
        int maxY = -1;

        // 엑셀 편집용 12x9 캔버스에서 실제로 칠하는 영역만 SO에 저장합니다.
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!IsPaintCell(grid[y, x]))
                {
                    continue;
                }

                minX = Mathf.Min(minX, x);
                maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y);
                maxY = Mathf.Max(maxY, y);
            }
        }

        if (maxX < minX || maxY < minY)
        {
            return null;
        }

        List<string> rows = new List<string>();
        for (int y = minY; y <= maxY; y++)
        {
            StringBuilder row = new StringBuilder();
            for (int x = minX; x <= maxX; x++)
            {
                row.Append(grid[y, x]);
            }

            rows.Add(row.ToString());
        }

        return new PatternBlock
        {
            Pivot = new Vector2Int(pivot.x - minX, pivot.y - minY),
            Rows = rows.ToArray()
        };
    }

    private static Dictionary<string, string> ReadSharedStrings(ZipArchive archive)
    {
        Dictionary<string, string> values = new Dictionary<string, string>();
        ZipArchiveEntry entry = GetArchiveEntry(archive, "xl/sharedStrings.xml");
        if (entry == null)
        {
            return values;
        }

        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XDocument document = LoadXml(entry);
        int index = 0;
        foreach (XElement item in document.Root.Elements(main + "si"))
        {
            values[index.ToString(CultureInfo.InvariantCulture)] = string.Concat(item.Descendants(main + "t").Select(t => t.Value));
            index++;
        }

        return values;
    }

    private static Dictionary<string, string> ReadWorksheetCells(ZipArchive archive, string worksheetPath, Dictionary<string, string> sharedStrings)
    {
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XDocument document = LoadXml(archive, worksheetPath);
        Dictionary<string, string> cells = new Dictionary<string, string>();

        foreach (XElement cell in document.Descendants(main + "c"))
        {
            string reference = (string)cell.Attribute("r");
            string type = (string)cell.Attribute("t");
            string value = string.Empty;

            if (type == "inlineStr")
            {
                value = string.Concat(cell.Descendants(main + "t").Select(t => t.Value));
            }
            else
            {
                value = cell.Element(main + "v") != null ? cell.Element(main + "v").Value : string.Empty;
                if (type == "s" && sharedStrings.TryGetValue(value, out string sharedValue))
                {
                    value = sharedValue;
                }
            }

            cells[reference] = value.Trim();
        }

        return cells;
    }

    private static void WriteWorkbook(string excelAssetPath, List<WeaponExcelRow> rows)
    {
        string fullPath = ToFullPath(excelAssetPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        using (ZipArchive archive = ZipFile.Open(fullPath, ZipArchiveMode.Create))
        {
            AddText(archive, "[Content_Types].xml", BuildContentTypes(rows.Count));
            AddText(archive, "_rels/.rels", BuildRootRelationships());
            AddText(archive, "xl/workbook.xml", BuildWorkbook(rows));
            AddText(archive, "xl/_rels/workbook.xml.rels", BuildWorkbookRelationships(rows.Count));
            AddText(archive, "xl/styles.xml", BuildStyles());

            for (int i = 0; i < rows.Count; i++)
            {
                AddText(archive, "xl/worksheets/sheet" + (i + 1) + ".xml", BuildWorksheet(rows[i]));
            }
        }
    }

    private static string BuildContentTypes(int sheetCount)
    {
        StringBuilder xml = new StringBuilder();
        xml.Append(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>");
        xml.Append(@"<Types xmlns=""http://schemas.openxmlformats.org/package/2006/content-types"">");
        xml.Append(@"<Default Extension=""rels"" ContentType=""application/vnd.openxmlformats-package.relationships+xml""/>");
        xml.Append(@"<Default Extension=""xml"" ContentType=""application/xml""/>");
        xml.Append(@"<Override PartName=""/xl/workbook.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml""/>");
        xml.Append(@"<Override PartName=""/xl/styles.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml""/>");
        for (int i = 1; i <= sheetCount; i++)
        {
            xml.Append(@"<Override PartName=""/xl/worksheets/sheet").Append(i).Append(@".xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml""/>");
        }

        xml.Append("</Types>");
        return xml.ToString();
    }

    private static string BuildRootRelationships()
    {
        return @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?><Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships""><Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"" Target=""xl/workbook.xml""/></Relationships>";
    }

    private static string BuildWorkbook(List<WeaponExcelRow> rows)
    {
        StringBuilder xml = new StringBuilder();
        xml.Append(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>");
        xml.Append(@"<workbook xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"" xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships""><sheets>");
        for (int i = 0; i < rows.Count; i++)
        {
            xml.Append(@"<sheet name=""").Append(XmlEscape(SanitizeSheetName(rows[i].Id))).Append(@""" sheetId=""").Append(i + 1).Append(@""" r:id=""rId").Append(i + 1).Append(@"""/>");
        }

        xml.Append("</sheets></workbook>");
        return xml.ToString();
    }

    private static string BuildWorkbookRelationships(int sheetCount)
    {
        StringBuilder xml = new StringBuilder();
        xml.Append(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>");
        xml.Append(@"<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">");
        for (int i = 1; i <= sheetCount; i++)
        {
            xml.Append(@"<Relationship Id=""rId").Append(i).Append(@""" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet").Append(i).Append(@".xml""/>");
        }

        xml.Append(@"<Relationship Id=""rId").Append(sheetCount + 1).Append(@""" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"" Target=""styles.xml""/>");
        xml.Append("</Relationships>");
        return xml.ToString();
    }

    private static string BuildStyles()
    {
        return @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<styleSheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"">
  <fonts count=""1""><font><sz val=""11""/><name val=""Calibri""/></font></fonts>
  <fills count=""5"">
    <fill><patternFill patternType=""none""/></fill>
    <fill><patternFill patternType=""gray125""/></fill>
    <fill><patternFill patternType=""solid""><fgColor rgb=""FFFFFFFF""/><bgColor indexed=""64""/></patternFill></fill>
    <fill><patternFill patternType=""solid""><fgColor rgb=""FF4A90E2""/><bgColor indexed=""64""/></patternFill></fill>
    <fill><patternFill patternType=""solid""><fgColor rgb=""FFE24B4A""/><bgColor indexed=""64""/></patternFill></fill>
  </fills>
  <borders count=""1""><border><left/><right/><top/><bottom/><diagonal/></border></borders>
  <cellStyleXfs count=""1""><xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""0""/></cellStyleXfs>
  <cellXfs count=""4"">
    <xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""0"" xfId=""0""/>
    <xf numFmtId=""0"" fontId=""0"" fillId=""2"" borderId=""0"" xfId=""0"" applyFill=""1""/>
    <xf numFmtId=""0"" fontId=""0"" fillId=""3"" borderId=""0"" xfId=""0"" applyFill=""1""/>
    <xf numFmtId=""0"" fontId=""0"" fillId=""4"" borderId=""0"" xfId=""0"" applyFill=""1""/>
  </cellXfs>
  <dxfs count=""2"">
    <dxf><fill><patternFill patternType=""solid""><fgColor rgb=""FF4A90E2""/><bgColor indexed=""64""/></patternFill></fill></dxf>
    <dxf><fill><patternFill patternType=""solid""><fgColor rgb=""FFE24B4A""/><bgColor indexed=""64""/></patternFill></fill></dxf>
  </dxfs>
  <cellStyles count=""1""><cellStyle name=""Normal"" xfId=""0"" builtinId=""0""/></cellStyles>
</styleSheet>";
    }

    private static string BuildWorksheet(WeaponExcelRow row)
    {
        Dictionary<string, string> cells = new Dictionary<string, string>
        {
            { "A1", "id" },
            { "B1", row.Id },
            { "A2", "sortOrder" },
            { "B2", row.SortOrder.ToString(CultureInfo.InvariantCulture) },
            { "A3", "displayName" },
            { "B3", row.DisplayName },
            { "A4", "category" },
            { "B4", row.Category },
            { "A5", "pattern" },
            { "B5", row.PatternType.ToString() },
            { "A6", "cooldown" },
            { "B6", row.Cooldown.ToString(CultureInfo.InvariantCulture) },
            { "A7", "range" },
            { "B7", row.Range.ToString(CultureInfo.InvariantCulture) },
            { "A8", "delaySeconds" },
            { "B8", row.DelaySeconds.ToString(CultureInfo.InvariantCulture) },
            { "A9", "pivotX" },
            { "B9", row.Pivot.x.ToString(CultureInfo.InvariantCulture) },
            { "A10", "pivotY" },
            { "B10", row.Pivot.y.ToString(CultureInfo.InvariantCulture) },
            { "A11", "description" },
            { "B11", row.Description },
            { "A12", "notes" },
            { "B12", row.Notes },
            { "A13", "lv1 pattern grid" },
            { "A25", "lv3 pattern grid" },
            { "A37", "lv5 pattern grid" }
        };

        cells["N1"] = "statusEffectType";
        cells["O1"] = row.StatusEffect.Type.ToString();
        cells["N2"] = "statusMagnitude";
        cells["O2"] = row.StatusEffect.Magnitude.ToString(CultureInfo.InvariantCulture);
        cells["N3"] = "statusDuration";
        cells["O3"] = row.StatusEffect.Duration.ToString(CultureInfo.InvariantCulture);
        cells["N4"] = "statusStackMode";
        cells["O4"] = row.StatusEffect.StackMode.ToString();

        AddPatternBlockCells(cells, PatternStartRow, row.Pivot, row.PatternRows);
        AddPatternBlockCells(cells, Level3PatternStartRow, row.Level3Pivot, row.Level3PatternRows ?? row.PatternRows);
        AddPatternBlockCells(cells, Level5PatternStartRow, row.Level5Pivot, row.Level5PatternRows ?? row.Level3PatternRows ?? row.PatternRows);

        return BuildSheetXml(cells);
    }

    private static void AddPatternBlockCells(Dictionary<string, string> cells, int startRow, Vector2Int pivot, string[] patternRows)
    {
        char[,] grid = new char[PatternRows, PatternColumns];
        for (int y = 0; y < PatternRows; y++)
        {
            for (int x = 0; x < PatternColumns; x++)
            {
                grid[y, x] = '0';
            }
        }

        string[] rows = patternRows ?? new[] { "2" };
        int targetPivotX = PatternColumns / 2;
        int targetPivotY = PatternRows / 2;
        // SO의 trim된 패턴을 엑셀에서는 중앙 pivot 기준으로 다시 펼쳐 보여줍니다.
        for (int y = 0; y < rows.Length; y++)
        {
            string sourceRow = rows[y] ?? string.Empty;
            for (int x = 0; x < sourceRow.Length; x++)
            {
                int targetX = targetPivotX + x - pivot.x;
                int targetY = targetPivotY + y - pivot.y;
                if (targetX < 0 || targetX >= PatternColumns || targetY < 0 || targetY >= PatternRows)
                {
                    continue;
                }

                grid[targetY, targetX] = NormalizePatternCell(sourceRow[x].ToString());
            }
        }

        if (targetPivotX >= 0 && targetPivotX < PatternColumns && targetPivotY >= 0 && targetPivotY < PatternRows)
        {
            grid[targetPivotY, targetPivotX] = '2';
        }

        for (int y = 0; y < PatternRows; y++)
        {
            for (int x = 0; x < PatternColumns; x++)
            {
                cells[CellName(PatternStartColumn + x, startRow + y)] = grid[y, x].ToString();
            }
        }
    }

    private static string BuildSheetXml(Dictionary<string, string> cells)
    {
        StringBuilder xml = new StringBuilder();
        xml.Append(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>");
        xml.Append(@"<worksheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main""><sheetData>");

        foreach (IGrouping<int, KeyValuePair<string, string>> row in cells.GroupBy(pair => CellRow(pair.Key)).OrderBy(group => group.Key))
        {
            xml.Append(@"<row r=""").Append(row.Key).Append(@""">");
            foreach (KeyValuePair<string, string> cell in row.OrderBy(pair => CellColumn(pair.Key)))
            {
                int styleIndex = GetBaseStyleIndex(cell.Key, cells);
                xml.Append(@"<c r=""").Append(cell.Key).Append(@""" s=""").Append(styleIndex).Append(@""" t=""inlineStr""><is><t>")
                    .Append(XmlEscape(cell.Value))
                    .Append("</t></is></c>");
            }

            xml.Append("</row>");
        }

        xml.Append("</sheetData>");
        AppendPatternConditionalFormatting(xml, PatternStartRow, 1);
        AppendPatternConditionalFormatting(xml, Level3PatternStartRow, 3);
        AppendPatternConditionalFormatting(xml, Level5PatternStartRow, 5);
        xml.Append("</worksheet>");
        return xml.ToString();
    }

    private static void AppendPatternConditionalFormatting(StringBuilder xml, int startRow, int priority)
    {
        string range = CellName(PatternStartColumn, startRow) + ":" + CellName(PatternStartColumn + PatternColumns - 1, startRow + PatternRows - 1);
        xml.Append(@"<conditionalFormatting sqref=""").Append(range).Append(@""">");
        xml.Append(@"<cfRule type=""cellIs"" dxfId=""0"" priority=""").Append(priority + 1).Append(@""" operator=""equal""><formula>1</formula></cfRule>");
        xml.Append(@"<cfRule type=""cellIs"" dxfId=""1"" priority=""").Append(priority).Append(@""" operator=""equal""><formula>2</formula></cfRule>");
        xml.Append("</conditionalFormatting>");
    }

    private static int GetBaseStyleIndex(string cellName, Dictionary<string, string> cells)
    {
        int column = CellColumn(cellName);
        int row = CellRow(cellName);
        return IsPatternCell(column, row) ? 1 : 0;
    }

    private static bool IsPatternCell(int column, int row)
    {
        return column >= PatternStartColumn
            && column < PatternStartColumn + PatternColumns
            && (IsRowInsidePattern(row, PatternStartRow)
                || IsRowInsidePattern(row, Level3PatternStartRow)
                || IsRowInsidePattern(row, Level5PatternStartRow));
    }

    private static bool IsRowInsidePattern(int row, int startRow)
    {
        return row >= startRow && row < startRow + PatternRows;
    }

    private static char NormalizePatternCell(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return '0';
        }

        char cell = value.Trim()[0];
        if (cell == '2' || cell == 'P' || cell == 'p')
        {
            return '2';
        }

        return IsPaintCell(cell) ? '1' : '0';
    }

    private static bool IsPaintCell(char cell)
    {
        return cell == '1' || cell == '2' || cell == 'X' || cell == 'x' || cell == '#';
    }

    private static List<WeaponExcelRow> CreateDefaultRows()
    {
        return new List<WeaponExcelRow>
        {
            CreateRow("meteor_cross", 0, "Meteor Cross", "Fire", WeaponPatternType.Grid, 8f, 1, 0f, "Marks a cross-like meteor strike pattern for heavy area control.", "Root control with a long cooldown.", new[] { "0001000", "0011100", "0111110", "1112111", "0111110", "0011100", "0001000" }),
            CreateRow("fire_surge", 1, "Fire Surge", "Fire", WeaponPatternType.Grid, 5f, 3, 0f, "Spreads flame runes through a jagged forward tile pattern.", "Fast short slow in a forward flame shape.", new[] { "0001000", "0011100", "0111110", "1111110", "0111100", "0011000", "0001000" }),
            CreateRow("arcane_lance", 2, "Arcane Lance", "Arcane", WeaponPatternType.Grid, 6f, 1, 0f, "Claims a precise magical beam line across the grid.", "Long beam that grows thicker with level.", new[] { "0001000", "0001000", "0001000", "0001000", "0001000", "0001000", "0001000" }),
            CreateRow("frost_wall", 3, "Frost Wall", "Frost", WeaponPatternType.Grid, 9f, 1, 0f, "Raises a line of icy runes to claim and control space.", "Wide root wall.", new[] { "0000000", "0000000", "0111110", "1112111", "0111110", "0000000", "0000000" }),
            CreateRow("lightning_chain", 4, "Lightning Chain", "Lightning", WeaponPatternType.Grid, 11f, 3, 0f, "Chains lightning through split or zigzag rune tiles.", "Wider diagonal chain pattern.", new[] { "1000001", "0100010", "0010100", "0001000", "0010100", "0100010", "1000001" }),
            CreateRow("crystal_bloom", 5, "Crystal Bloom", "Crystal", WeaponPatternType.Grid, 7.5f, 1, 0f, "Blooms crystal energy around the caster in a compact pattern.", "Uses Crystals crossfade 2.", new[] { "0000000", "0011100", "0111110", "0112110", "0111110", "0011100", "0000000" }),
            CreateRow("ground_rupture", 6, "Ground Rupture", "Earth", WeaponPatternType.Grid, 8.5f, 1, 0f, "Breaks the ground in a staggered magic pattern.", "Uses Ground AOE explosion.", new[] { "0001000", "0011100", "0111110", "1112111", "0111110", "0011100", "0001000" }),
            CreateRow("red_energy_burst", 7, "Red Energy Burst", "Arcane", WeaponPatternType.Grid, 10f, 1, 0f, "Releases a volatile red energy burst for heavy area control.", "Uses Red energy explosion.", new[] { "0011100", "0111110", "1111111", "1112111", "1111111", "0111110", "0011100" })
        };
    }

    private static WeaponExcelRow CreateRow(string id, int sortOrder, string displayName, string category, WeaponPatternType patternType, float cooldown, int range, float delaySeconds, string description, string notes, string[] patternRows)
    {
        WeaponExcelRow row = new WeaponExcelRow
        {
            Id = id,
            SortOrder = sortOrder,
            DisplayName = displayName,
            Category = category,
            PatternType = patternType,
            Cooldown = cooldown,
            Range = range,
            DelaySeconds = delaySeconds,
            Pivot = new Vector2Int(2, 2),
            Description = description,
            Notes = notes,
            PatternRows = patternRows,
            StatusEffect = GetDefaultStatusEffect(id)
        };

        row.Level3PatternRows = ExpandPatternRows(row.PatternRows, row.Pivot, 1, out row.Level3Pivot);
        row.Level5PatternRows = ExpandPatternRows(row.PatternRows, row.Pivot, 2, out row.Level5Pivot);
        return row;
    }

    private static string[] ExpandPatternRows(string[] sourceRows, Vector2Int sourcePivot, int amount, out Vector2Int expandedPivot)
    {
        PatternBlock source = TrimPatternBlock(ToPatternGrid(sourceRows), sourceRows[0].Length, sourceRows.Length, sourcePivot);
        if (source == null)
        {
            expandedPivot = sourcePivot;
            return sourceRows;
        }

        List<Vector2Int> paintCells = new List<Vector2Int>();
        for (int y = 0; y < source.Rows.Length; y++)
        {
            string row = source.Rows[y];
            for (int x = 0; x < row.Length; x++)
            {
                if (IsPaintCell(row[x]))
                {
                    paintCells.Add(new Vector2Int(x, y));
                }
            }
        }

        HashSet<Vector2Int> expanded = new HashSet<Vector2Int>(paintCells);
        // 일자형 패턴은 양 끝만 늘리고, 면형 패턴은 외곽을 한 겹씩 확장합니다.
        if (IsLineLike(paintCells, source.Pivot))
        {
            ExpandLineLikePattern(expanded, paintCells, amount);
        }
        else
        {
            ExpandShapePattern(expanded, amount);
        }

        return BuildRowsFromCells(expanded, source.Pivot, out expandedPivot);
    }

    private static StatusEffectSpec GetDefaultStatusEffect(string weaponId)
    {
        switch ((weaponId ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "wind_slash":
            case "axe":
                return new StatusEffectSpec(StatusEffectType.Root, 0f, 0.28f, StatusEffectStackMode.RefreshDuration);
            case "tidal_lance":
            case "water_cannon":
                return new StatusEffectSpec(StatusEffectType.Slow, 0.92f, 0.4f, StatusEffectStackMode.ReplaceIfStronger);
            case "crystal_spike":
            case "drill":
                return new StatusEffectSpec(StatusEffectType.Slow, 0.75f, 0.7f, StatusEffectStackMode.ReplaceIfStronger);
            case "arcane_lance":
            case "laser":
                return new StatusEffectSpec(StatusEffectType.Slow, 0.72f, 2f, StatusEffectStackMode.ReplaceIfStronger);
            case "meteor_cross":
            case "bomb":
                return new StatusEffectSpec(StatusEffectType.Root, 0f, 2.5f, StatusEffectStackMode.RefreshDuration);
            case "fire_surge":
            case "flamethrower":
                return new StatusEffectSpec(StatusEffectType.Slow, 0.82f, 2.25f, StatusEffectStackMode.ReplaceIfStronger);
            case "frost_wall":
            case "freezer":
                return new StatusEffectSpec(StatusEffectType.Root, 0f, 3.5f, StatusEffectStackMode.RefreshDuration);
            case "lightning_chain":
            case "split_shot":
                return new StatusEffectSpec(StatusEffectType.Slow, 0.64f, 3.25f, StatusEffectStackMode.ReplaceIfStronger);
            case "crystal_bloom":
            case "summon_circle":
                return new StatusEffectSpec(StatusEffectType.Root, 0f, 2f, StatusEffectStackMode.RefreshDuration);
            case "ground_rupture":
                return new StatusEffectSpec(StatusEffectType.Root, 0f, 4.25f, StatusEffectStackMode.RefreshDuration);
            case "red_energy_burst":
                return new StatusEffectSpec(StatusEffectType.Slow, 0.58f, 5f, StatusEffectStackMode.ReplaceIfStronger);
            default:
                return default(StatusEffectSpec);
        }
    }

    private static char[,] ToPatternGrid(string[] rows)
    {
        int height = rows != null ? rows.Length : 0;
        int width = rows != null && rows.Length > 0 ? rows.Max(row => row != null ? row.Length : 0) : 0;
        char[,] grid = new char[height, width];
        for (int y = 0; y < height; y++)
        {
            string row = rows[y] ?? string.Empty;
            for (int x = 0; x < width; x++)
            {
                grid[y, x] = x < row.Length ? NormalizePatternCell(row[x].ToString()) : '0';
            }
        }

        return grid;
    }

    private static bool IsLineLike(List<Vector2Int> cells, Vector2Int pivot)
    {
        if (cells.Count == 0)
        {
            return false;
        }

        return cells.All(cell => cell.x == pivot.x) || cells.All(cell => cell.y == pivot.y);
    }

    private static void ExpandLineLikePattern(HashSet<Vector2Int> expanded, List<Vector2Int> cells, int amount)
    {
        int minX = cells.Min(cell => cell.x);
        int maxX = cells.Max(cell => cell.x);
        int minY = cells.Min(cell => cell.y);
        int maxY = cells.Max(cell => cell.y);
        bool vertical = minX == maxX;

        for (int i = 1; i <= amount; i++)
        {
            if (vertical)
            {
                expanded.Add(new Vector2Int(minX, minY - i));
                expanded.Add(new Vector2Int(minX, maxY + i));
            }
            else
            {
                expanded.Add(new Vector2Int(minX - i, minY));
                expanded.Add(new Vector2Int(maxX + i, minY));
            }
        }
    }

    private static void ExpandShapePattern(HashSet<Vector2Int> expanded, int amount)
    {
        for (int step = 0; step < amount; step++)
        {
            List<Vector2Int> current = expanded.ToList();
            foreach (Vector2Int cell in current)
            {
                for (int y = -1; y <= 1; y++)
                {
                    for (int x = -1; x <= 1; x++)
                    {
                        expanded.Add(new Vector2Int(cell.x + x, cell.y + y));
                    }
                }
            }
        }
    }

    private static string[] BuildRowsFromCells(HashSet<Vector2Int> cells, Vector2Int pivot, out Vector2Int newPivot)
    {
        int minX = Mathf.Min(cells.Min(cell => cell.x), pivot.x);
        int maxX = Mathf.Max(cells.Max(cell => cell.x), pivot.x);
        int minY = Mathf.Min(cells.Min(cell => cell.y), pivot.y);
        int maxY = Mathf.Max(cells.Max(cell => cell.y), pivot.y);
        List<string> rows = new List<string>();

        for (int y = minY; y <= maxY; y++)
        {
            StringBuilder row = new StringBuilder();
            for (int x = minX; x <= maxX; x++)
            {
                row.Append(x == pivot.x && y == pivot.y ? '2' : cells.Contains(new Vector2Int(x, y)) ? '1' : '0');
            }

            rows.Add(row.ToString());
        }

        newPivot = new Vector2Int(pivot.x - minX, pivot.y - minY);
        return rows.ToArray();
    }
    private static void AddText(ZipArchive archive, string path, string text)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path);
        using (StreamWriter writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
        {
            writer.Write(text);
        }
    }

    private static XDocument LoadXml(ZipArchive archive, string path)
    {
        ZipArchiveEntry entry = GetArchiveEntry(archive, path);
        if (entry == null)
        {
            throw new FileNotFoundException("Missing xlsx part: " + path);
        }

        return LoadXml(entry);
    }

    private static ZipArchiveEntry GetArchiveEntry(ZipArchive archive, string path)
    {
        if (archive == null || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string forwardSlashPath = path.Replace('\\', '/');
        return archive.GetEntry(forwardSlashPath)
            ?? archive.GetEntry(forwardSlashPath.Replace('/', '\\'));
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        using (Stream stream = entry.Open())
        {
            return XDocument.Load(stream);
        }
    }

    private static string GetCell(Dictionary<string, string> cells, string key, string fallback = "")
    {
        return cells.TryGetValue(key, out string value) ? value : fallback;
    }

    private static WeaponPatternType ParsePattern(string value)
    {
        return System.Enum.TryParse(value, true, out WeaponPatternType parsed) ? parsed : WeaponPatternType.Grid;
    }

    private static float ParseFloat(string value, float fallback)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) ? parsed : fallback;
    }

    private static int ParseInt(string value, int fallback)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : fallback;
    }

    private static T ParseEnum<T>(string value, T fallback) where T : struct
    {
        return System.Enum.TryParse(value, true, out T parsed) ? parsed : fallback;
    }

    private static string CellName(int column, int row)
    {
        return ColumnName(column) + row.ToString(CultureInfo.InvariantCulture);
    }

    private static string ColumnName(int column)
    {
        StringBuilder name = new StringBuilder();
        while (column > 0)
        {
            column--;
            name.Insert(0, (char)('A' + column % 26));
            column /= 26;
        }

        return name.ToString();
    }

    private static int CellRow(string cellName)
    {
        string digits = new string(cellName.Where(char.IsDigit).ToArray());
        return ParseInt(digits, 0);
    }

    private static int CellColumn(string cellName)
    {
        int column = 0;
        foreach (char c in cellName)
        {
            if (!char.IsLetter(c))
            {
                break;
            }

            column = column * 26 + char.ToUpperInvariant(c) - 'A' + 1;
        }

        return column;
    }

    private static string SanitizeSheetName(string value)
    {
        char[] invalid = { ':', '\\', '/', '?', '*', '[', ']' };
        string sanitized = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return sanitized.Length > 31 ? sanitized.Substring(0, 31) : sanitized;
    }

    private static string XmlEscape(string value)
    {
        return System.Security.SecurityElement.Escape(value ?? string.Empty);
    }

    private static string ToFullPath(string assetPath)
    {
        return Path.Combine(Directory.GetCurrentDirectory(), assetPath);
    }

    private class WeaponExcelRow
    {
        public string Id;
        public int SortOrder;
        public string DisplayName;
        public string Category;
        public WeaponPatternType PatternType;
        public float Cooldown;
        public int Range;
        public float DelaySeconds;
        public Vector2Int Pivot;
        public string Description;
        public string Notes;
        public string[] PatternRows;
        public Vector2Int Level3Pivot;
        public string[] Level3PatternRows;
        public Vector2Int Level5Pivot;
        public string[] Level5PatternRows;
        public StatusEffectSpec StatusEffect;
    }

    private class PatternBlock
    {
        public Vector2Int Pivot;
        public string[] Rows;
    }
}
