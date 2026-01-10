using System.Diagnostics;
using System.Text;

class Program
{
    static string RootDir = Directory.GetCurrentDirectory();
    static string ArtifactsDir = Path.Combine(RootDir, "artifacts");

    static void Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: dotnet run -- <os> <arch>");
            return;
        }

        string targetOs = args[0].ToLower(); // linux, osx, android, ios, maccatalyst, browser
        string arch = args[1].ToLower();     // x64, arm64, wasm
        string emscriptenVersion = args.Length > 2 ? args[2].ToLower() : string.Empty; // emscripten version

        string buildDir = Path.Combine(RootDir, "build", $"{targetOs}-{arch}");
        string installDir = Path.Combine(ArtifactsDir, $"{targetOs}-{arch}");
        string depInstallDir = Path.Combine(buildDir, "dep-install");

        if ($"{targetOs}-{arch}" == "browser-wasm")
        {
            installDir = Path.Combine(installDir, $"emscripten-{emscriptenVersion}");
        }

        Directory.CreateDirectory(buildDir);
        Directory.CreateDirectory(installDir);

        // 1. Build libde265 (Static)
        BuildLibde265(targetOs, arch, buildDir, depInstallDir);

        // 2. Build dav1d (Static)
        BuildDav1d(targetOs, arch, buildDir, depInstallDir);

        // 3. Build libheif (Dynamic, linking others statically)
        BuildLibHeif(targetOs, arch, buildDir, installDir, depInstallDir);

        CopyStaticLibraries(targetOs, arch, installDir, depInstallDir);
    }

    static void BuildLibde265(string os, string arch, string buildDir, string installDir)
    {
        string srcDir = Path.Combine(RootDir, "libde265");
        string bDir = Path.Combine(buildDir, "libde265");
        Directory.CreateDirectory(bDir);

        var cmakeArgs = GetCMakeBaseArgs(os, arch, installDir);
        cmakeArgs += @"
            -DBUILD_SHARED_LIBS=OFF
            -DENABL_DECODER=OFF
            -DENABL_SHERLOCK265=OFF
            -DENABLE_SDL=OFF
            ";

        if ($"{os}-{arch}" == "browser-wasm")
        {
            RunProcess("emcmake", $"cmake {cmakeArgs} \"{srcDir}\"", bDir);
        }
        else
        {
            RunProcess("cmake", $"{cmakeArgs} \"{srcDir}\"", bDir);
        }

        RunProcess("cmake", "--build . --config Release --target install", bDir);
    }

    static void BuildDav1d(string os, string arch, string buildDir, string installDir)
    {
        string srcDir = Path.Combine(RootDir, "dav1d");
        string bDir = Path.Combine(buildDir, "dav1d");

        // 基础 Meson 命令
        string mesonArgs = @$"
            setup ""{bDir}"" ""{srcDir}"" 
            --default-library=static 
            --buildtype=release 
            --prefix=""{installDir}""
            --libdir=""{Path.Combine(installDir, "lib")}""
            --includedir=""{Path.Combine(installDir, "include")}""
            -Denable_tools=false
            -Denable_tests=false
            ";

        string? crossFilePath = (os, arch) switch
        {
            ("linux", "arm64") => Path.Combine(srcDir, "package/crossfiles/aarch64-linux.meson"),
            ("ios", "arm64") => Path.Combine(srcDir, "package/crossfiles/arm64-iPhoneOS.meson"),
            ("iossimulator", "arm64") => Path.Combine(RootDir, "tools/dav1d/package/crossfiles/arm64-iPhoneSimulator.meson"),
            ("iossimulator", "x64") => Path.Combine(srcDir, "package/crossfiles/x86_64-iPhoneSimulator.meson"),
            ("maccatalyst", "arm64") => Path.Combine(RootDir, "tools/dav1d/package/crossfiles/arm64-maccatalyst.meson"),
            ("maccatalyst", "x64") => Path.Combine(RootDir, "tools/dav1d/package/crossfiles/x86_64-maccatalyst.meson"),
            ("android", "arm64") => Path.Combine(srcDir, "package/crossfiles/aarch64-android.meson"),
            ("android", "x64") => Path.Combine(srcDir, "package/crossfiles/x86_64-android.meson"),
            ("browser", "wasm") => Path.Combine(RootDir, "tools/dav1d/package/crossfiles/wasm32.meson"),
            (_, _) => null
        };
        if (crossFilePath is not null)
        {
            mesonArgs += $" --cross-file \"{crossFilePath}\"";
        }

        RunProcess("meson", mesonArgs, RootDir);
        RunProcess("ninja", $"-C \"{bDir}\" install", RootDir);
    }

    static void BuildLibHeif(string os, string arch, string buildDir, string installDir, string depDir)
    {
        string srcDir = Path.Combine(RootDir, "libheif");
        string bDir = Path.Combine(buildDir, "libheif");
        string libDir = Path.Combine(depDir, "lib");
        string includeDir = Path.Combine(depDir, "include");
        Directory.CreateDirectory(bDir);

        var cmakeArgs = GetCMakeBaseArgs(os, arch, installDir);

        cmakeArgs += $" -DCMAKE_PREFIX_PATH=\"{depDir}\"";
        cmakeArgs += @$" 
            -DWITH_DAV1D=ON 
            -DWITH_DAV1D_PLUGIN=OFF
            -DWITH_LIBDE265=ON 
            -DWITH_LIBDE265_PLUGIN=OFF
            -DENABLE_PLUGIN_LOADING=OFF 
            -DWITH_EXAMPLES=OFF 
            -DWITH_EXAMPLE_HEIF_THUMB=OFF 
            -DWITH_EXAMPLE_HEIF_VIEW=OFF 
            -DBUILD_TESTING=OFF 
            -DWITH_AOM_DECODER=OFF
            -DWITH_AOM_ENCODER=OFF 
            -DWITH_X265=OFF
            -DWITH_X264=OFF
            -DWITH_GDK_PIXBUF=OFF
            -DWITH_LIBSHARPYUV=OFF            
            -DBUILD_DOCUMENTATION=OFF
            -DLIBDE265_LIBRARY=""{Path.Combine(libDir, "libde265.a")}""            
            -DLIBDE265_INCLUDE_DIR=""{includeDir}""
            -DDAV1D_LIBRARY=""{Path.Combine(libDir, "libdav1d.a")}""            
            -DDAV1D_INCLUDE_DIR=""{includeDir}""
            ";

        if (os == "ios" || os == "iossimulator" || $"{os}-{arch}" == "browser-wasm")
        {
            cmakeArgs += @" -DBUILD_SHARED_LIBS=OFF";
        }

        if (os == "android")
        {
            cmakeArgs += " -Dld-version-script=OFF";
        }

        if ($"{os}-{arch}" == "browser-wasm")
        {
            cmakeArgs += @" 
                -DENABLE_MULTITHREADING_SUPPORT=OFF
                -DCMAKE_CXX_FLAGS=""-sALLOW_MEMORY_GROWTH=1""";
        }

        if ($"{os}-{arch}" == "browser-wasm")
        {
            RunProcess("emcmake", $"cmake {cmakeArgs} \"{srcDir}\"", bDir);
        }
        else
        {
            RunProcess("cmake", $"{cmakeArgs} \"{srcDir}\"", bDir);
        }
        // 使用 --parallel 提升速度，并确保 Release 配置
        RunProcess("cmake", "--build . --config Release --target install --parallel 4", bDir);

        StripBinary(os, arch, installDir);
    }

    static string GetCMakeBaseArgs(string os, string arch, string installDir)
    {
        string args = $@"
            -DCMAKE_INSTALL_PREFIX=""{installDir}""
            -DCMAKE_BUILD_TYPE=Release
            -DCMAKE_POSITION_INDEPENDENT_CODE=ON
            -DCMAKE_POLICY_VERSION_MINIMUM=3.5
            ";

        if (os == "linux" && arch == "arm64")
        {
            args += @"
                -DCMAKE_SYSTEM_NAME=Linux
                -DCMAKE_SYSTEM_PROCESSOR=aarch64
                -DCMAKE_C_COMPILER=aarch64-linux-gnu-gcc
                -DCMAKE_CXX_COMPILER=aarch64-linux-gnu-g++
                ";
        }
        else if (os == "osx")
        {
            string CMAKE_OSX_ARCHITECTURES = arch switch
            {
                "arm64" => "arm64",
                "x64" => "x86_64",
                _ => arch
            };

            args += " -DCMAKE_SYSTEM_NAME=Darwin";
            args += $" -DCMAKE_OSX_ARCHITECTURES={CMAKE_OSX_ARCHITECTURES}";
            args += " -DCMAKE_OSX_DEPLOYMENT_TARGET=11.0";
        }
        else if (os == "ios" || os == "iossimulator")
        {
            string CMAKE_OSX_ARCHITECTURES = arch switch
            {
                "arm64" => "arm64",
                "x64" => "x86_64",
                _ => arch
            };

            args += " -DCMAKE_SYSTEM_NAME=iOS";
            args += os == "ios" ? " -DCMAKE_OSX_SYSROOT=iphoneos" : " -DCMAKE_OSX_SYSROOT=iphonesimulator";
            args += $" -DCMAKE_OSX_ARCHITECTURES={CMAKE_OSX_ARCHITECTURES}";
            args += " -DCMAKE_OSX_DEPLOYMENT_TARGET=12.0";
        }
        else if (os == "maccatalyst")
        {
            string CMAKE_OSX_ARCHITECTURES = arch switch
            {
                "arm64" => "arm64",
                "x64" => "x86_64",
                _ => arch
            };

            // Mac Catalyst 的标准 CMake 参数
            args += @$"
                -DCMAKE_SYSTEM_NAME=Darwin
                -DCMAKE_OSX_SYSROOT=macosx
                -DCMAKE_C_FLAGS=""--target={CMAKE_OSX_ARCHITECTURES}-apple-ios13.1-macabi""
                -DCMAKE_CXX_FLAGS=""--target={CMAKE_OSX_ARCHITECTURES}-apple-ios13.1-macabi""
                ";
            args += $" -DCMAKE_OSX_ARCHITECTURES={CMAKE_OSX_ARCHITECTURES}";
        }
        else if (os == "android")
        {
            string? ndk = Environment.GetEnvironmentVariable("ANDROID_NDK_LATEST_HOME");
            string ANDROID_ABI = arch switch
            {
                "arm64" => "arm64-v8a",
                "x64" => "x86_64",
                _ => arch
            };
            args += $@"
                -DCMAKE_SYSTEM_NAME=Android
                -DCMAKE_TOOLCHAIN_FILE=""{ndk}/build/cmake/android.toolchain.cmake""
                -DANDROID_ABI={ANDROID_ABI}
                -DANDROID_PLATFORM=android-21
                ";
        }

        return args;
    }

    static void RunProcess(string fileName, string args, string workingDir)
    {
        args = CompactLines(args);
        Console.WriteLine($"[EXEC] {fileName} {args} in {workingDir}");
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        process.WaitForExit();
        if (process.ExitCode != 0) throw new Exception($"Process failed with exit code {process.ExitCode}");
    }

    static string CompactLines(string input)
    {
        var result = new StringBuilder();
        bool previousLineWasContent = false;

        using (var reader = new StringReader(input))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                if (previousLineWasContent)
                    result.Append(' ');

                result.Append(trimmed);
                previousLineWasContent = true;
            }
        }

        return result.ToString();
    }

    static void StripBinary(string os, string arch, string installDir)
    {
        if (os != "android" && os != "linux" && os != "osx")
        {
            return;
        }

        foreach (var file in FindBinaries(installDir, os))
        {
            var (tool, args) = GetStripTool(os, arch, file);
            Console.WriteLine($"[STRIP] {file}");

            try
            {
                RunProcess(tool, $"{args} \"{file}\"", RootDir);
            }
            catch
            {
                Console.WriteLine($"[WARN] Strip failed: {file}");
            }
        }
    }

    static List<string> FindBinaries(string installDir, string os)
    {
        var results = new List<string>();

        if (Directory.Exists(Path.Combine(installDir, "lib")))
        {
            results.AddRange(Directory.GetFiles(
                Path.Combine(installDir, "lib"),
                "*",
                SearchOption.AllDirectories
            ).Where(f =>
                f.EndsWith(".so") ||
                f.EndsWith(".so.0") ||
                f.EndsWith(".dylib") ||
                f.EndsWith(".a")
            ));
        }

        return results;
    }

    static (string tool, string args) GetStripTool(string os, string arch, string file)
    {
        bool isStatic = file.EndsWith(".a");

        if (os == "android")
            return ("llvm-strip", isStatic ? "-S" : "--strip-debug");

        if (os == "linux" && arch == "arm64")
            return ("aarch64-linux-gnu-strip", isStatic ? "-S" : "--strip-unneeded");

        if (os == "linux")
            return ("strip", isStatic ? "-S" : "--strip-unneeded");

        // Apple 平台（Mach-O）
        return ("strip", "-x");
    }

    static void CopyStaticLibraries(string os, string arch, string installDir, string depDir)
    {
        if (os != "ios" && os != "iossimulator" && $"{os}-{arch}" != "browser-wasm")
        {
            return;
        }

        string libDe265 = Path.Combine(depDir, "lib/libde265.a");
        string libDav1d = Path.Combine(depDir, "lib/libdav1d.a");
        string newlibDe265 = Path.Combine(installDir, "lib/libde265.a");
        string newlibDav1d = Path.Combine(installDir, "lib/libdav1d.a");

        File.Copy(libDe265, newlibDe265, true);
        File.Copy(libDav1d, newlibDav1d, true);

        Console.WriteLine("Static libraries copy successfully.");
    }
}