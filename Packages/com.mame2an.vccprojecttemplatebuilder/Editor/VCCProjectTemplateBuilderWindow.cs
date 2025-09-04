// File: Assets/Mame2an/VCCProjectTemplateBuilder/Editor/VCCProjectTemplateBuilderWindow.cs
// VCC User Template Builder（自動更新/強制作成オプション削除版）

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Mame2an.VCC
{
    public class VCCProjectTemplateBuilderWindow : EditorWindow
    {
        private const string kPrefsKey = "Mame2an.VCC.TemplateBuilder.";

        [Serializable]
        private class AssetNode
        {
            public string name;          // 表示名（フォルダ名）
            public string relPath;       // "Assets/..." の相対パス
            public List<AssetNode> children = new List<AssetNode>();
        }

        private Dictionary<string, bool> _foldout = new Dictionary<string, bool>();

        // ツリーを構築（深さ制限付き）
        private AssetNode BuildAssetsTree(int depth = 2)
        {
            string ap = Path.GetFullPath("Assets");
            var root = new AssetNode { name = "Assets", relPath = "Assets" };
            if (!Directory.Exists(ap)) return root;

            void AddChildren(AssetNode parent, string fullDir, int currentDepth)
            {
                if (currentDepth > depth) return;
                foreach (var dir in Directory.GetDirectories(fullDir))
                {
                    var folderName = Path.GetFileName(dir);
                    if (folderName.StartsWith(".")) continue;

                    var node = new AssetNode
                    {
                        name = folderName,
                        relPath = RelativeFromProject(dir) // 既存のユーティリティ関数を利用
                    };
                    parent.children.Add(node);

                    AddChildren(node, dir, currentDepth + 1);
                }
            }

            AddChildren(root, ap, 1);
            return root;
        }

        private enum BaseTemplateType { Unity2022World, Unity2022Avatar, Unity2019World, Unity2019Avatar }
        private BaseTemplateType baseTemplate = BaseTemplateType.Unity2022World;

        // 基本情報
        private string displayName = "My Project Template";
        private string description = "My favorite starting project for VRChat.";
        private bool autoMeta = true; // name/version 自動生成
        private string internalNameManual = "user.vrchat.template.myTemplate";
        private string versionManual = "1.0.0";

        // Assets
        private bool copyAllAssets = true;
        private Vector2 scroll;
        private List<string> selectedAssetFolders = new List<string>();

        // Packages
        private bool copyPackagesFolder = true;

        // 上書き/消去
        private bool overwriteExisting = true;
        private int selectedExistingIndex = -1;

        // Unity バージョン表示関連
        private bool setUnityVersion = true;               // package.json に unityVersion を書く
        private string unityVersionText = "2022.3.22f1";
        private bool includeProjectVersionTxt = true;       // ProjectVersion.txt を含める（0.0.0回避）

        [MenuItem("Tools/Mame2an/VCC Template Builder")]
        public static void Open()
        {
            var w = GetWindow<VCCProjectTemplateBuilderWindow>("VCC Template Builder");
            w.minSize = new Vector2(560, 780);
            w.LoadPrefs();
            w.Focus();
        }

        private void OnEnable()
        {
            if (selectedAssetFolders.Count == 0) RefreshAssetFolderCandidates();
        }

        private void RefreshAssetFolderCandidates()
        {
            selectedAssetFolders.Clear();
            var assetsPath = Path.GetFullPath("Assets");
            if (!Directory.Exists(assetsPath)) return;
            foreach (var dir in Directory.GetDirectories(assetsPath))
            {
                var name = Path.GetFileName(dir);
                if (name.StartsWith(".") || name.Equals("Editor", StringComparison.OrdinalIgnoreCase)) continue;
                selectedAssetFolders.Add(RelativeFromProject(dir));
            }
        }

        private void OnGUI()
        {
            using (var scrollScope = new EditorGUILayout.ScrollViewScope(scroll))
            {
                scroll = scrollScope.scrollPosition;

                EditorGUILayout.LabelField("VCC ユーザーテンプレート生成", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("現在のプロジェクトから VCC の “User Templates” を作成します。", MessageType.Info);

                // ベース
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("ベーステンプレート選択", EditorStyles.boldLabel);
                baseTemplate = (BaseTemplateType)EditorGUILayout.EnumPopup("Base Template", baseTemplate);
                EditorGUILayout.HelpBox(GetBaseTemplateHint(), MessageType.None);

                // メタ
                EditorGUILayout.Space();
                displayName = EditorGUILayout.TextField("表示名 (displayName)", displayName);
                description = EditorGUILayout.TextField("説明 (description)", description);

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("内部名 / バージョン", EditorStyles.boldLabel);
                autoMeta = EditorGUILayout.ToggleLeft("内部名・バージョンをタイムコードで自動生成する（推奨）", autoMeta);
                if (autoMeta)
                {
                    EditorGUILayout.LabelField("内部名 (name)", GenerateInternalNamePreview());
                    EditorGUILayout.LabelField("バージョン (version)", GenerateVersionPreview());
                }
                else
                {
                    internalNameManual = EditorGUILayout.TextField("内部名 (name)", internalNameManual);
                    versionManual = EditorGUILayout.TextField("バージョン (version)", versionManual);
                }

                // Assets
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Assets のコピー範囲", EditorStyles.boldLabel);
                copyAllAssets = EditorGUILayout.ToggleLeft("Assets を全部コピー", copyAllAssets);
                using (new EditorGUI.DisabledScope(copyAllAssets))
                {
                    if (GUILayout.Button("Assets 直下のフォルダ一覧を更新")) RefreshAssetFolderCandidates();
                    EditorGUILayout.LabelField("コピー対象（チェック＝含める）");
                    DrawAssetFolderSelector();
                }

                // Packages
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Packages のコピー", EditorStyles.boldLabel);
                copyPackagesFolder = EditorGUILayout.ToggleLeft("Packages をコピー (manifest.json / packages-lock.json 等)", copyPackagesFolder);

                // Unity Version 表示＆固定
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Unity バージョン（VCC 表示）", EditorStyles.boldLabel);
                setUnityVersion = EditorGUILayout.ToggleLeft("package.json に unityVersion を書き込む", setUnityVersion);
                string suggestedUnity = (baseTemplate == BaseTemplateType.Unity2019Avatar || baseTemplate == BaseTemplateType.Unity2019World)
                    ? "2019.4.31f1" : "2022.3.22f1";
                using (new EditorGUILayout.HorizontalScope())
                {
                    unityVersionText = EditorGUILayout.TextField("unityVersion", string.IsNullOrEmpty(unityVersionText) ? suggestedUnity : unityVersionText);
                    if (GUILayout.Button("推奨に戻す", GUILayout.Width(110))) unityVersionText = suggestedUnity;
                }
                includeProjectVersionTxt = EditorGUILayout.ToggleLeft("ProjectSettings/ProjectVersion.txt をテンプレートに含める（0.0.0 回避）", includeProjectVersionTxt);

                // 上書き/消去
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("テンプレートの上書き・消去", EditorStyles.boldLabel);
                overwriteExisting = EditorGUILayout.ToggleLeft("同名フォルダが存在する場合は上書きする", overwriteExisting);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("現在の表示名のテンプレートを消去", GUILayout.Height(24))) DeleteTemplateByDisplayName(displayName);
                    if (GUILayout.Button("出力フォルダを開く", GUILayout.Height(24)))
                    {
                        var path = GetTemplateOutputPath(displayName);
                        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                        EditorUtility.RevealInFinder(path);
                    }
                }
                DrawExistingTemplateList();

                // 出力先
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("出力先", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(GetTemplatesRootInfoText(), MessageType.None);

                // 実行
                EditorGUILayout.Space();
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("テンプレートを作成", GUILayout.Height(32)))
                    {
                        try
                        {
                            CreateTemplate();
                            EditorUtility.DisplayDialog("完了", "テンプレートを作成しました。VCC の新規プロジェクトで確認できます。", "OK");
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError(ex);
                            EditorUtility.DisplayDialog("エラー", ex.Message, "OK");
                        }
                    }
                    if (GUILayout.Button("テンプレート出力フォルダを開く", GUILayout.Height(32)))
                    {
                        var root = GetTemplatesRootPath();
                        Directory.CreateDirectory(root);
                        EditorUtility.RevealInFinder(root);
                    }
                }

                if (GUI.changed) SavePrefs();
            }
        }

        private void DeleteTemplateByDisplayName(string name)
        {
            var path = GetTemplateOutputPath(name);
            if (!Directory.Exists(path))
            {
                EditorUtility.DisplayDialog("情報", "指定のテンプレートフォルダは存在しません。", "OK");
                return;
            }
            if (!EditorUtility.DisplayDialog("確認", $"「{Path.GetFileName(path)}」を削除します。元に戻せません。", "削除", "キャンセル")) return;

            try
            {
                FileUtil.DeleteFileOrDirectory(path);
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("削除完了", "テンプレートを削除しました。", "OK");
            }
            catch (Exception ex) { Debug.LogError(ex); EditorUtility.DisplayDialog("エラー", ex.Message, "OK"); }
        }

        private void DrawExistingTemplateList()
        {
            var root = GetTemplatesRootPath();
            if (!Directory.Exists(root)) { EditorGUILayout.HelpBox("テンプレートフォルダがまだありません。", MessageType.None); return; }

            var dirs = Directory.GetDirectories(root).OrderBy(Path.GetFileName).ToArray();
            if (dirs.Length == 0) { EditorGUILayout.HelpBox("登録済みテンプレートはありません。", MessageType.None); return; }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("既存テンプレート一覧", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                selectedExistingIndex = EditorGUILayout.Popup(selectedExistingIndex < 0 ? 0 : selectedExistingIndex, dirs.Select(Path.GetFileName).ToArray());
                if (GUILayout.Button("開く", GUILayout.Width(80)))
                {
                    if (dirs.Length > 0) EditorUtility.RevealInFinder(dirs[Mathf.Clamp(selectedExistingIndex, 0, dirs.Length - 1)]);
                }
                if (GUILayout.Button("消去", GUILayout.Width(80)))
                {
                    if (dirs.Length > 0)
                    {
                        var target = dirs[Mathf.Clamp(selectedExistingIndex, 0, dirs.Length - 1)];
                        if (EditorUtility.DisplayDialog("確認", $"「{Path.GetFileName(target)}」を削除します。元に戻せません。", "削除", "キャンセル"))
                        {
                            try { FileUtil.DeleteFileOrDirectory(target); AssetDatabase.Refresh(); selectedExistingIndex = -1; }
                            catch (Exception e) { Debug.LogError(e); EditorUtility.DisplayDialog("エラー", e.Message, "OK"); }
                        }
                    }
                }
            }
        }

        private string GetBaseTemplateHint()
        {
            switch (baseTemplate)
            {
                case BaseTemplateType.Unity2022World: return "Unity 2022.x / Worlds 用（推奨）";
                case BaseTemplateType.Unity2022Avatar: return "Unity 2022.x / Avatars 用（推奨）";
                case BaseTemplateType.Unity2019World: return "Unity 2019.4 LTS / Worlds 用（互換用途）";
                case BaseTemplateType.Unity2019Avatar: return "Unity 2019.4 LTS / Avatars 用（互換用途）";
            }
            return "";
        }

        private void DrawAssetFolderSelector()
        {
            var tree = BuildAssetsTree();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("すべて展開", GUILayout.Width(90))) SetAllFoldout(tree, true);
                if (GUILayout.Button("すべて折りたたみ", GUILayout.Width(110))) SetAllFoldout(tree, false);
                GUILayout.FlexibleSpace();
            }

            EditorGUILayout.Space(2);

            // ★ここで1回だけ描画する
            _rowIndex = 0; // 縞模様を毎回リセット
            foreach (var child in tree.children.OrderBy(n => n.name, StringComparer.OrdinalIgnoreCase))
            {
                DrawNodeRecursive(child, indent: 0);
            }
        }

        private List<string> GetAssetsRootSubfolders()
        {
            var result = new List<string>();
            var ap = Path.GetFullPath("Assets");
            if (!Directory.Exists(ap)) return result;

            // 深さ2まで探索
            void AddDirs(string baseDir, int currentDepth, int maxDepth)
            {
                if (currentDepth > maxDepth) return;
                foreach (var dir in Directory.GetDirectories(baseDir))
                {
                    var name = Path.GetFileName(dir);
                    if (name.StartsWith(".")) continue;
                    var rel = RelativeFromProject(dir);
                    result.Add(rel);
                    AddDirs(dir, currentDepth + 1, maxDepth); // サブフォルダを追加で探索
                }
            }

            AddDirs(ap, 1, 2); // Assets直下＋さらに1階層
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        private void CreateTemplate()
        {
            ValidateInputs();

            var templateDir = GetTemplateOutputPath(displayName);
            if (Directory.Exists(templateDir))
            {
                if (!overwriteExisting)
                    throw new Exception($"同名のテンプレートフォルダが既に存在します：\n{templateDir}\n\n「同名フォルダが存在する場合は上書きする」にチェックを入れるか、事前に消去してください。");
                FileUtil.DeleteFileOrDirectory(templateDir);
            }
            Directory.CreateDirectory(templateDir);

            // package.json
            WritePackageJson(templateDir);

            // Packages
            if (copyPackagesFolder) CopyPackagesFolder(templateDir);

            // vpm-manifest.json（存在時）
            CopyVpmManifest(templateDir);

            // Assets
            CopyAssets(templateDir);

            // ProjectSettings
            CopyProjectSettings(templateDir);

            AssetDatabase.Refresh();
        }

        private void ValidateInputs()
        {
            if (string.IsNullOrWhiteSpace(displayName)) throw new Exception("表示名 (displayName) を入力してください。");

            // Unity メジャー版の整合性チェック
            int expectedMajor = (baseTemplate == BaseTemplateType.Unity2019Avatar || baseTemplate == BaseTemplateType.Unity2019World) ? 2019 : 2022;
            int currentMajor = GetUnityMajorVersion();
            if (currentMajor != expectedMajor)
                throw new Exception($"現在の Unity バージョン（{Application.unityVersion}）とベーステンプレート（{expectedMajor}）が一致しません。");

            // Avatar/World 依存の確認（vpm-manifest.json）
            string vpmPath = Path.GetFullPath("Packages/vpm-manifest.json");
            if (File.Exists(vpmPath))
            {
                string vpmText = File.ReadAllText(vpmPath, Encoding.UTF8);
                bool hasAvatars = vpmText.Contains("\"com.vrchat.avatars\"");
                bool hasWorlds = vpmText.Contains("\"com.vrchat.worlds\"");
                bool needAvatar = (baseTemplate == BaseTemplateType.Unity2022Avatar || baseTemplate == BaseTemplateType.Unity2019Avatar);

                if (needAvatar && !hasAvatars)
                    throw new Exception("選択は Avatar 用ですが、vpm-manifest.json に `com.vrchat.avatars` がありません。");
                if (!needAvatar && !hasWorlds)
                    throw new Exception("選択は World 用ですが、vpm-manifest.json に `com.vrchat.worlds` がありません。");
            }

            // 手入力モードでは name/version の最低限チェック
            if (!autoMeta)
            {
                if (string.IsNullOrWhiteSpace(internalNameManual)) throw new Exception("内部名 (name) を入力してください。");
                if (!internalNameManual.Contains(".")) throw new Exception("内部名 (name) は逆ドメイン形式を推奨します。");
                if (string.IsNullOrWhiteSpace(versionManual)) versionManual = "1.0.0";
            }

            if ((setUnityVersion || includeProjectVersionTxt) && string.IsNullOrWhiteSpace(unityVersionText))
            {
                unityVersionText = (baseTemplate == BaseTemplateType.Unity2019Avatar || baseTemplate == BaseTemplateType.Unity2019World)
                    ? "2019.4.31f1" : "2022.3.22f1";
            }
        }

        private int GetUnityMajorVersion()
        {
            try { var parts = Application.unityVersion.Split('.'); if (parts.Length > 0 && int.TryParse(parts[0], out int m)) return m; }
            catch { }
            return 2022;
        }

        private void WritePackageJson(string templateDir)
        {
            bool isAvatar = (baseTemplate == BaseTemplateType.Unity2022Avatar || baseTemplate == BaseTemplateType.Unity2019Avatar);
            string baseLabel = GetBaseTemplateDisplayName(baseTemplate);
            string nameOut = autoMeta ? GenerateInternalNamePreview() : internalNameManual.Trim();
            string verOut = autoMeta ? GenerateVersionPreview() : versionManual.Trim();

            var json = new StringBuilder();
            json.AppendLine("{");
            json.AppendLine($"  \"name\": \"{EscapeJson(nameOut)}\",");
            json.AppendLine($"  \"displayName\": \"{EscapeJson(displayName)}\",");
            if (!string.IsNullOrWhiteSpace(description)) json.AppendLine($"  \"description\": \"{EscapeJson(description)}\",");
            json.AppendLine($"  \"version\": \"{EscapeJson(verOut)}\",");
            json.AppendLine($"  \"category\": \"ProjectTemplate\",");

            if (setUnityVersion)
            {
                string uv = string.IsNullOrWhiteSpace(unityVersionText)
                    ? ((baseTemplate == BaseTemplateType.Unity2019Avatar || baseTemplate == BaseTemplateType.Unity2019World) ? "2019.4.31f1" : "2022.3.22f1")
                    : unityVersionText.Trim();
                json.AppendLine($"  \"unityVersion\": \"{EscapeJson(uv)}\",");
            }

            json.AppendLine($"  \"keywords\": [\"vrchat\", \"{(isAvatar ? "avatar" : "world")}\"],");
            json.AppendLine($"  \"mame2anBase\": \"{EscapeJson(baseLabel)}\"");
            json.AppendLine("}");

            File.WriteAllText(Path.Combine(templateDir, "package.json"), json.ToString(), new UTF8Encoding(false));
        }

        private static string GetBaseTemplateDisplayName(BaseTemplateType t)
        {
            switch (t)
            {
                case BaseTemplateType.Unity2022World: return "Unity 2022 World Project";
                case BaseTemplateType.Unity2022Avatar: return "Unity 2022 Avatar Project";
                case BaseTemplateType.Unity2019World: return "Unity 2019 World Project";
                case BaseTemplateType.Unity2019Avatar: return "Unity 2019 Avatar Project";
            }
            return "Unknown";
        }

        private string GenerateInternalNamePreview()
        {
            string kind = (baseTemplate == BaseTemplateType.Unity2022Avatar || baseTemplate == BaseTemplateType.Unity2019Avatar) ? "avatar" : "world";
            return $"user.vrchat.template.{kind}.{DateTime.UtcNow:yyyyMMdd_HHmmss}";
        }
        private string GenerateVersionPreview() => "1.0." + DateTime.UtcNow.ToString("yyyyMMddHHmmss");

        // Packages
        private void CopyPackagesFolder(string templateDir)
        {
            var srcPackages = Path.GetFullPath("Packages");
            if (!Directory.Exists(srcPackages)) return;

            var dstPackages = Path.Combine(templateDir, "Packages");
            Directory.CreateDirectory(dstPackages);

            foreach (var file in Directory.GetFiles(srcPackages, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(file);
                if (name.Equals("vpm-manifest.json", StringComparison.OrdinalIgnoreCase)) continue;
                File.Copy(file, Path.Combine(dstPackages, name), overwrite: true);
            }
            foreach (var dir in Directory.GetDirectories(srcPackages, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(dir);
                CopyDirectoryFiltered(dir, Path.Combine(dstPackages, name), new[] { "/.git", "/.svn" });
            }
        }

        private void CopyVpmManifest(string templateDir)
        {
            var src = Path.GetFullPath(Path.Combine(Application.dataPath, "../Packages/vpm-manifest.json"));
            if (!File.Exists(src)) return;

            var dstDir = Path.Combine(templateDir, "Packages");
            Directory.CreateDirectory(dstDir);
            var dst = Path.Combine(dstDir, "vpm-manifest.json");
            File.Copy(src, dst, overwrite: true);
        }

        // Assets / ProjectSettings
        private void CopyAssets(string templateDir)
        {
            var dstAssets = Path.Combine(templateDir, "Assets");
            if (Directory.Exists(dstAssets)) FileUtil.DeleteFileOrDirectory(dstAssets);
            Directory.CreateDirectory(dstAssets);

            if (copyAllAssets)
            {
                CopyDirectoryFiltered(Path.GetFullPath("Assets"), dstAssets, new[] { "/.git", "/.svn" });
            }
            else
            {
                // 深さ2まで列挙された候補（親子の関係判定に使う）
                var candidates = GetAssetsRootSubfolders()
                    .Select(s => s.Replace("\\", "/").TrimEnd('/'))
                    .ToList();

                // 正規化した「選択済み」セット
                var selected = new HashSet<string>(
                    selectedAssetFolders.Select(s => s.Replace("\\", "/").TrimEnd('/')),
                    StringComparer.OrdinalIgnoreCase
                );

                // 2-1) トップレベル選択（＝選択済みの中から、さらに上位の選択がないもの）
                var topSelected = selected
                    .Where(s => !selected.Any(o => o.Length < s.Length && s.StartsWith(o + "/", StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(s => s.Count(c => c == '/')) // 浅い順
                    .ToList();

                foreach (var rel in topSelected)
                {
                    var fullSrc = Path.GetFullPath(rel);
                    if (!Directory.Exists(fullSrc)) continue;

                    // 2-2) この親の配下にある「候補の子」で、未選択のものを除外リストへ
                    var exclude = new List<string> { "/.git", "/.svn" };
                    foreach (var child in candidates)
                    {
                        if (child.Length > rel.Length && child.StartsWith(rel + "/", StringComparison.OrdinalIgnoreCase))
                        {
                            // child が選択されていなければ除外
                            if (!selected.Contains(child))
                            {
                                var relInParent = child.Substring(rel.Length).Replace("\\", "/"); // 先頭は '/' のはず
                                if (!string.IsNullOrEmpty(relInParent))
                                    exclude.Add(relInParent); // 例: "/BoothFolderIcons"
                            }
                        }
                    }

                    // 2-3) 親を「相対パスのまま」コピーし、未選択の子は exclude で除外
                    var dstPath = Path.Combine(templateDir, rel.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(dstPath)!);
                    CopyDirectoryFiltered(fullSrc, dstPath, exclude.ToArray());
                }
            }
        }

        private static List<string> CollapseSelections(IEnumerable<string> rels)
        {
            var norm = rels
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Replace("\\", "/").TrimEnd('/'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s.Count(c => c == '/')) // 階層の浅い順（親を先に）
                .ToList();

            var result = new List<string>();
            foreach (var p in norm)
            {
                bool isChildOfExisting = result.Any(parent =>
                    p.Length > parent.Length &&
                    p.StartsWith(parent + "/", StringComparison.OrdinalIgnoreCase));
                if (!isChildOfExisting) result.Add(p);
            }
            return result;
        }

        private void CopyProjectSettings(string templateDir)
        {
            var src = Path.GetFullPath("ProjectSettings");
            var dst = Path.Combine(templateDir, "ProjectSettings");
            if (!Directory.Exists(src)) { if (includeProjectVersionTxt) Directory.CreateDirectory(dst); }
            else
            {
                Directory.CreateDirectory(dst);
                foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                {
                    var rel = file.Substring(src.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (rel.Replace("\\", "/").Equals("ProjectVersion.txt", StringComparison.OrdinalIgnoreCase)) continue; // 自前で出力
                    var outPath = Path.Combine(dst, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                    File.Copy(file, outPath, overwrite: true);
                }
            }

            // 任意で ProjectVersion.txt を作成
            if (includeProjectVersionTxt)
            {
                Directory.CreateDirectory(dst);
                var pvPath = Path.Combine(dst, "ProjectVersion.txt");
                string ver = string.IsNullOrWhiteSpace(unityVersionText)
                    ? ((baseTemplate == BaseTemplateType.Unity2019Avatar || baseTemplate == BaseTemplateType.Unity2019World) ? "2019.4.31f1" : "2022.3.22f1")
                    : unityVersionText.Trim();
                var sb = new StringBuilder();
                sb.AppendLine($"m_EditorVersion: {ver}");
                sb.AppendLine($"m_EditorVersionWithRevision: {ver} (000000000000)");
                File.WriteAllText(pvPath, sb.ToString(), new UTF8Encoding(false));
            }
        }

        private static void CopyDirectoryFiltered(string srcDir, string dstDir, string[] excludePatterns = null)
        {
            excludePatterns ??= Array.Empty<string>();
            foreach (var dir in Directory.GetDirectories(srcDir, "*", SearchOption.AllDirectories))
            {
                var rel = dir.Substring(srcDir.Length).Replace("\\", "/");
                if (excludePatterns.Any(p => rel.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                Directory.CreateDirectory(Path.Combine(dstDir, rel.TrimStart('/', '\\')));
            }
            foreach (var file in Directory.GetFiles(srcDir, "*", SearchOption.AllDirectories))
            {
                var rel = file.Substring(srcDir.Length).Replace("\\", "/");
                if (excludePatterns.Any(p => rel.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                var outPath = Path.Combine(dstDir, rel.TrimStart('/', '\\'));
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                File.Copy(file, outPath, overwrite: true);
            }
        }

        private static string GetTemplatesRootPath()
        {
#if UNITY_EDITOR_WIN
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VRChatCreatorCompanion", "Templates");
#else
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRChatCreatorCompanion", "Templates");
#endif
        }
        private string GetTemplatesRootInfoText()
        {
#if UNITY_EDITOR_WIN
            string win = GetTemplatesRootPath();
            return $"Windows: {win}\nmacOS: ~/Library/Application Support/VRChatCreatorCompanion/Templates";
#else
            return $"macOS: ~/Library/Application Support/VRChatCreatorCompanion/Templates\nWindows: %LOCALAPPDATA%/VRChatCreatorCompanion/Templates";
#endif
        }
        private static string GetTemplateOutputPath(string displayName)
        {
            string folderName = SanitizeFolderName(displayName);
            string root = GetTemplatesRootPath();
            Directory.CreateDirectory(root);
            return Path.Combine(root, folderName);
        }

        private static string SanitizeFolderName(string s)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = s.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
            var result = new string(chars).Trim();
            if (string.IsNullOrEmpty(result)) result = "Template";
            return result;
        }
        private static string EscapeJson(string s) => s == null ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        private static string RelativeFromProject(string fullPath)
        {
            var projectRoot = Path.GetFullPath(".");
            var rel = Path.GetFullPath(fullPath).Replace("\\", "/");
            projectRoot = projectRoot.Replace("\\", "/");
            if (rel.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase)) rel = rel.Substring(projectRoot.Length).TrimStart('/');
            return rel;
        }

        private void SavePrefs()
        {
            EditorPrefs.SetString(kPrefsKey + "displayName", displayName);
            EditorPrefs.SetString(kPrefsKey + "description", description);
            EditorPrefs.SetBool(kPrefsKey + "autoMeta", autoMeta);
            EditorPrefs.SetString(kPrefsKey + "internalNameManual", internalNameManual);
            EditorPrefs.SetString(kPrefsKey + "versionManual", versionManual);
            EditorPrefs.SetBool(kPrefsKey + "copyAllAssets", copyAllAssets);
            EditorPrefs.SetString(kPrefsKey + "selectedAssetFolders", string.Join("|", selectedAssetFolders));
            EditorPrefs.SetBool(kPrefsKey + "copyPackagesFolder", copyPackagesFolder);
            EditorPrefs.SetInt(kPrefsKey + "baseTemplate", (int)baseTemplate);
            EditorPrefs.SetBool(kPrefsKey + "overwriteExisting", overwriteExisting);
            EditorPrefs.SetBool(kPrefsKey + "setUnityVersion", setUnityVersion);
            EditorPrefs.SetString(kPrefsKey + "unityVersionText", unityVersionText);
            EditorPrefs.SetBool(kPrefsKey + "includeProjectVersionTxt", includeProjectVersionTxt);
        }

        private void LoadPrefs()
        {
            displayName = EditorPrefs.GetString(kPrefsKey + "displayName", displayName);
            description = EditorPrefs.GetString(kPrefsKey + "description", description);
            autoMeta = EditorPrefs.GetBool(kPrefsKey + "autoMeta", true);
            internalNameManual = EditorPrefs.GetString(kPrefsKey + "internalNameManual", internalNameManual);
            versionManual = EditorPrefs.GetString(kPrefsKey + "versionManual", versionManual);
            copyAllAssets = EditorPrefs.GetBool(kPrefsKey + "copyAllAssets", true);

            var joined = EditorPrefs.GetString(kPrefsKey + "selectedAssetFolders", "");
            selectedAssetFolders = string.IsNullOrEmpty(joined) ? new List<string>() : joined.Split('|').ToList();

            copyPackagesFolder = EditorPrefs.GetBool(kPrefsKey + "copyPackagesFolder", true);
            baseTemplate = (BaseTemplateType)EditorPrefs.GetInt(kPrefsKey + "baseTemplate", (int)BaseTemplateType.Unity2022World);
            overwriteExisting = EditorPrefs.GetBool(kPrefsKey + "overwriteExisting", true);

            string defUnity = (baseTemplate == BaseTemplateType.Unity2019Avatar || baseTemplate == BaseTemplateType.Unity2019World)
                ? "2019.4.31f1" : "2022.3.22f1";
            setUnityVersion = EditorPrefs.GetBool(kPrefsKey + "setUnityVersion", true);
            unityVersionText = EditorPrefs.GetString(kPrefsKey + "unityVersionText", defUnity);
            includeProjectVersionTxt = EditorPrefs.GetBool(kPrefsKey + "includeProjectVersionTxt", true);
        }

        private int _rowIndex = 0; // 既存 or 追加

        private void DrawNodeRecursive(AssetNode node, int indent)
        {
            float lineH = Mathf.Ceil(EditorGUIUtility.singleLineHeight + 2f);
            Rect rowRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                                                    GUILayout.Height(lineH), GUILayout.ExpandWidth(true));

            var even = (_rowIndex % 2) == 0;
            EditorGUI.DrawRect(rowRect, even ? new Color(0.20f, 0.20f, 0.20f) : new Color(0.22f, 0.22f, 0.22f));

            float x = rowRect.x + indent * 16f;
            float y = rowRect.y + (lineH - 16f) * 0.5f;

            bool hasChildren = node.children.Count > 0;

            // フォルダの矢印（Foldout）
            if (hasChildren)
            {
                bool opened = GetFold(node.relPath);
                opened = EditorGUI.Foldout(new Rect(x, y, 16f, 16f), opened, GUIContent.none, false);
                _foldout[node.relPath] = opened;
            }
            x += 16f; // 矢印スペース（子無しなら空白）

            // チェックボックス
            bool isChecked = selectedAssetFolders.Contains(node.relPath);
            bool next = EditorGUI.Toggle(new Rect(x, y, 16f, 16f), isChecked);
            x += 20f;

            // ラベル（チェック時は濃いめの青）
            var labelRect = new Rect(x, rowRect.y, rowRect.width - (x - rowRect.x) - 6f, lineH);
            var prevColor = GUI.color;
            if (isChecked) GUI.color = new Color(0.68f, 0.85f, 0.9f); // SteelBlue
            EditorGUI.LabelField(labelRect, node.name, EditorStyles.label);
            GUI.color = prevColor;

            // ラベルクリックで折りたたみ切替（矢印と同等の挙動）
            if (hasChildren && Event.current.type == EventType.MouseDown && labelRect.Contains(Event.current.mousePosition))
            {
                _foldout[node.relPath] = !GetFold(node.relPath);
                Event.current.Use();
                Repaint();
            }

            // チェック状態の反映（Altで親のみ、それ以外は配下も）
            if (next != isChecked)
            {
                if (Event.current.alt || node.children.Count == 0)
                    SetSelected(node.relPath, next);
                else
                    ToggleWithChildren(node, next);
            }

            _rowIndex++; // 次の行へ

            // 4) 子ノード
            if (node.children.Count > 0 && GetFold(node.relPath))
            {
                foreach (var child in node.children.OrderBy(n => n.name, StringComparer.OrdinalIgnoreCase))
                    DrawNodeRecursive(child, indent + 1);
            }
        }

        private void SetSelected(string relPath, bool on)
        {
            if (on)
            {
                if (!selectedAssetFolders.Contains(relPath)) selectedAssetFolders.Add(relPath);
            }
            else
            {
                selectedAssetFolders.Remove(relPath);
            }
        }

        private void ToggleWithChildren(AssetNode node, bool on)
        {
            SetSelected(node.relPath, on);
            foreach (var c in node.children) ToggleWithChildren(c, on);
        }

        private bool GetFold(string key) => _foldout.TryGetValue(key, out var v) ? v : false;

        private void SetAllFoldout(AssetNode node, bool open)
        {
            _foldout[node.relPath] = open;
            foreach (var c in node.children) SetAllFoldout(c, open);
        }
    }
}
