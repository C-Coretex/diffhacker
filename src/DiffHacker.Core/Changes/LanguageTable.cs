namespace DiffHacker.Core.Changes;

/// <summary>
/// Maps a filename to a language name, and does nothing else.
/// <para>
/// §0.2.3 makes the application language-agnostic: "the only per-language thing the app does is
/// tag a file with its detected language as metadata". This table is that one thing. It is a
/// lookup, hand-written and hand-owned rather than a dependency, because a dependency for a
/// dictionary buys coverage we do not need and a supply-chain edge we would rather not have.
/// </para>
/// <para>
/// An unrecognised name returns null. Guessing would put a wrong label in the prompt Iteration 7
/// sends, and a wrong label is worse than no label.
/// </para>
/// </summary>
public static class LanguageTable
{
    /// <summary>
    /// Files whose whole name identifies the language, checked before the extension. Ordinal
    /// ignore-case, because <c>Dockerfile</c> and <c>dockerfile</c> are both common.
    /// </summary>
    private static readonly Dictionary<string, string> ByFileName =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Dockerfile"] = "Dockerfile",
            ["Containerfile"] = "Dockerfile",
            ["Makefile"] = "Makefile",
            ["GNUmakefile"] = "Makefile",
            ["CMakeLists.txt"] = "CMake",
            ["Gemfile"] = "Ruby",
            ["Rakefile"] = "Ruby",
            ["Podfile"] = "Ruby",
            ["Vagrantfile"] = "Ruby",
            ["Brewfile"] = "Ruby",
            ["Jenkinsfile"] = "Groovy",
            ["go.mod"] = "Go Module",
            ["go.sum"] = "Go Module",
            ["Cargo.lock"] = "TOML",
            ["package-lock.json"] = "JSON",
            [".gitignore"] = "Git Config",
            [".gitattributes"] = "Git Config",
            [".gitmodules"] = "Git Config",
            [".editorconfig"] = "EditorConfig",
            [".npmrc"] = "INI",
            [".env"] = "Dotenv",
            ["LICENSE"] = "Plain Text",
            ["NOTICE"] = "Plain Text",
        };

    /// <summary>
    /// Extension to language. Keys include the leading dot and are matched ordinal ignore-case.
    /// Compound extensions such as <c>.tar.gz</c> are not modelled: the last dot wins.
    /// </summary>
    private static readonly Dictionary<string, string> ByExtension =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // .NET
            [".cs"] = "C#",
            [".csx"] = "C#",
            [".razor"] = "Razor",
            [".cshtml"] = "Razor",
            [".vb"] = "Visual Basic",
            [".fs"] = "F#",
            [".fsi"] = "F#",
            [".fsx"] = "F#",
            [".csproj"] = "MSBuild",
            [".fsproj"] = "MSBuild",
            [".vbproj"] = "MSBuild",
            [".props"] = "MSBuild",
            [".targets"] = "MSBuild",
            [".slnx"] = "MSBuild",
            [".sln"] = "MSBuild",

            // Web
            [".ts"] = "TypeScript",
            [".tsx"] = "TypeScript",
            [".mts"] = "TypeScript",
            [".cts"] = "TypeScript",
            [".js"] = "JavaScript",
            [".jsx"] = "JavaScript",
            [".mjs"] = "JavaScript",
            [".cjs"] = "JavaScript",
            [".vue"] = "Vue",
            [".svelte"] = "Svelte",
            [".astro"] = "Astro",
            [".html"] = "HTML",
            [".htm"] = "HTML",
            [".xhtml"] = "HTML",
            [".css"] = "CSS",
            [".scss"] = "Sass",
            [".sass"] = "Sass",
            [".less"] = "Less",
            [".styl"] = "Stylus",

            // JVM
            [".java"] = "Java",
            [".kt"] = "Kotlin",
            [".kts"] = "Kotlin",
            [".groovy"] = "Groovy",
            [".gradle"] = "Gradle",
            [".scala"] = "Scala",
            [".sc"] = "Scala",
            [".sbt"] = "Scala",
            [".clj"] = "Clojure",
            [".cljs"] = "Clojure",
            [".cljc"] = "Clojure",

            // Systems
            [".c"] = "C",
            [".h"] = "C/C++ Header",
            [".cc"] = "C++",
            [".cpp"] = "C++",
            [".cxx"] = "C++",
            [".c++"] = "C++",
            [".hh"] = "C/C++ Header",
            [".hpp"] = "C/C++ Header",
            [".hxx"] = "C/C++ Header",
            [".ixx"] = "C++",
            [".m"] = "Objective-C",
            [".mm"] = "Objective-C++",
            [".swift"] = "Swift",
            [".rs"] = "Rust",
            [".go"] = "Go",
            [".zig"] = "Zig",
            [".d"] = "D",
            [".nim"] = "Nim",
            [".v"] = "V",
            [".odin"] = "Odin",
            [".asm"] = "Assembly",
            [".s"] = "Assembly",

            // Scripting
            [".py"] = "Python",
            [".pyi"] = "Python",
            [".pyw"] = "Python",
            [".rb"] = "Ruby",
            [".erb"] = "Ruby",
            [".rake"] = "Ruby",
            [".php"] = "PHP",
            [".pl"] = "Perl",
            [".pm"] = "Perl",
            [".lua"] = "Lua",
            [".r"] = "R",
            [".jl"] = "Julia",
            [".dart"] = "Dart",
            [".ex"] = "Elixir",
            [".exs"] = "Elixir",
            [".erl"] = "Erlang",
            [".hrl"] = "Erlang",
            [".hs"] = "Haskell",
            [".lhs"] = "Haskell",
            [".ml"] = "OCaml",
            [".mli"] = "OCaml",
            [".elm"] = "Elm",
            [".cr"] = "Crystal",
            [".nix"] = "Nix",
            [".tcl"] = "Tcl",

            // Shell
            [".sh"] = "Shell",
            [".bash"] = "Shell",
            [".zsh"] = "Shell",
            [".fish"] = "Fish",
            [".ps1"] = "PowerShell",
            [".psm1"] = "PowerShell",
            [".psd1"] = "PowerShell",
            [".bat"] = "Batch",
            [".cmd"] = "Batch",

            // Data and config
            [".json"] = "JSON",
            [".jsonc"] = "JSON",
            [".json5"] = "JSON",
            [".yaml"] = "YAML",
            [".yml"] = "YAML",
            [".toml"] = "TOML",
            [".ini"] = "INI",
            [".cfg"] = "INI",
            [".conf"] = "Config",
            [".properties"] = "Properties",
            [".xml"] = "XML",
            [".xsd"] = "XML",
            [".xsl"] = "XML",
            [".xaml"] = "XAML",
            [".plist"] = "XML",
            [".csv"] = "CSV",
            [".tsv"] = "TSV",
            [".proto"] = "Protocol Buffers",
            [".graphql"] = "GraphQL",
            [".gql"] = "GraphQL",
            [".avsc"] = "Avro",
            [".tf"] = "Terraform",
            [".tfvars"] = "Terraform",
            [".hcl"] = "HCL",
            [".bicep"] = "Bicep",

            // Query and markup
            [".sql"] = "SQL",
            [".psql"] = "SQL",
            [".md"] = "Markdown",
            [".mdx"] = "MDX",
            [".markdown"] = "Markdown",
            [".rst"] = "reStructuredText",
            [".adoc"] = "AsciiDoc",
            [".asciidoc"] = "AsciiDoc",
            [".tex"] = "LaTeX",
            [".txt"] = "Plain Text",
            [".ipynb"] = "Jupyter Notebook",

            // Assets that show up in diffs
            [".svg"] = "SVG",
            [".png"] = "Image",
            [".jpg"] = "Image",
            [".jpeg"] = "Image",
            [".gif"] = "Image",
            [".webp"] = "Image",
            [".ico"] = "Image",
            [".woff"] = "Font",
            [".woff2"] = "Font",
            [".ttf"] = "Font",
            [".otf"] = "Font",
        };

    /// <summary>
    /// The language for a repository-relative path, or null when nothing recognises it.
    /// </summary>
    public static string? Detect(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        // Always split on '/': these are git paths, which use forward slashes on every platform.
        var lastSlash = path.LastIndexOf('/');
        var fileName = lastSlash >= 0 ? path[(lastSlash + 1)..] : path;

        if (fileName.Length == 0)
        {
            return null;
        }

        if (ByFileName.TryGetValue(fileName, out var byName))
        {
            return byName;
        }

        // A dot in the first position is part of the name, not an extension: ".gitignore" is
        // handled above, and ".unknownrc" has no extension to look up.
        var lastDot = fileName.LastIndexOf('.');
        if (lastDot <= 0 || lastDot == fileName.Length - 1)
        {
            return null;
        }

        return ByExtension.TryGetValue(fileName[lastDot..], out var byExtension) ? byExtension : null;
    }
}
