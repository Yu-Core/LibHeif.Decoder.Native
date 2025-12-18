using System.Diagnostics;

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

        string targetOs = args[0].ToLower(); // windows, linux, osx, android, ios, maccatalyst, browser-wasm
        string arch = args[1].ToLower();     // x64, arm64

        string buildDir = Path.Combine(RootDir, "build", $"{targetOs}-{arch}");
        string installDir = Path.Combine(ArtifactsDir, $"{targetOs}-{arch}");
        string depInstallDir = Path.Combine(buildDir, "dep-install");

        Directory.CreateDirectory(buildDir);
        Directory.CreateDirectory(installDir);

        // 1. Build libde265 (Static)
        BuildLibde265(targetOs, arch, buildDir, depInstallDir);

        // 2. Build dav1d (Static)
        BuildDav1d(targetOs, arch, buildDir, depInstallDir);

        // 3. Build libheif (Dynamic, linking others statically)
        BuildLibHeif(targetOs, arch, buildDir, installDir, depInstallDir);
    }

    static void BuildLibde265(string os, string arch, string buildDir, string installDir)
    {
        string srcDir = Path.Combine(RootDir, "libde265");
        string bDir = Path.Combine(buildDir, "libde265");
        Directory.CreateDirectory(bDir);

        var cmakeArgs = GetCMakeBaseArgs(os, arch, installDir);
        cmakeArgs += " -DBUILD_SHARED_LIBS=OFF -DENABLE_SHARED=OFF -DENABLE_STATIC=ON -DDISABLE_SHERLOCK265=ON -DBUILD_TOOLS=OFF";

        RunProcess("cmake", $"{cmakeArgs} \"{srcDir}\"", bDir);
        RunProcess("cmake", "--build . --config Release --target install", bDir);
    }

    static void BuildDav1d(string os, string arch, string buildDir, string installDir)
    {
        // dav1d 使用 meson，在某些交叉编译场景非常复杂
        // 为保证“直接能用”，这里在 Windows/Linux/OSX 原生编译，交叉编译使用特定的 cross-files
        string srcDir = Path.Combine(RootDir, "dav1d");
        string bDir = Path.Combine(buildDir, "dav1d");

        string mesonArgs = $"setup \"{bDir}\" \"{srcDir}\" --default-library=static --buildtype=release --prefix=\"{installDir}\" -Denable_tools=false -Denable_tests=false";

        if (os == "linux" && arch == "arm64")
        {
            File.WriteAllText("cross_linux_arm64.ini", "[binaries]\nc = 'aarch64-linux-gnu-gcc'\ncpp = 'aarch64-linux-gnu-g++'\nar = 'aarch64-linux-gnu-ar'\n[host_machine]\nsystem = 'linux'\ncpu_family = 'aarch64'\ncpu = 'aarch64'\nendian = 'little'");
            mesonArgs += " --cross-file ../../cross_linux_arm64.ini";
        }
        else if (os == "browser-wasm")
        {
            RunProcess("emmeson", mesonArgs, RootDir);
            RunProcess("ninja", $"-C \"{bDir}\" install", RootDir);
            return;
        }

        RunProcess("meson", mesonArgs, RootDir);
        RunProcess("ninja", $"-C \"{bDir}\" install", RootDir);
    }

    static void BuildLibHeif(string os, string arch, string buildDir, string installDir, string depDir)
    {
        string srcDir = Path.Combine(RootDir, "libheif");
        string bDir = Path.Combine(buildDir, "libheif");
        Directory.CreateDirectory(bDir);

        var cmakeArgs = GetCMakeBaseArgs(os, arch, installDir);
        bool isWasm = (os == "browser-wasm");

        cmakeArgs += $" -DCMAKE_PREFIX_PATH=\"{depDir}\"";
        cmakeArgs += " -DWITH_DAV1D=ON -DWITH_LIBDE265=ON -DENABLE_PLUGIN_LOADING=OFF -DWITH_EXAMPLES=OFF";
        cmakeArgs += isWasm ? " -DBUILD_SHARED_LIBS=OFF" : " -DBUILD_SHARED_LIBS=ON";

        RunProcess("cmake", $"{cmakeArgs} \"{srcDir}\"", bDir);
        RunProcess("cmake", "--build . --config Release --target install", bDir);
    }

    static string GetCMakeBaseArgs(string os, string arch, string installDir)
    {
        string args = $"-DCMAKE_INSTALL_PREFIX=\"{installDir}\" -DCMAKE_BUILD_TYPE=Release -DCMAKE_POSITION_INDEPENDENT_CODE=ON";

        if (os == "windows")
        {
            args += arch == "arm64" ? " -A ARM64" : " -A x64";
        }
        else if (os == "linux" && arch == "arm64")
        {
            args += " -DCMAKE_SYSTEM_NAME=Linux -DCMAKE_SYSTEM_PROCESSOR=aarch64 -DCMAKE_C_COMPILER=aarch64-linux-gnu-gcc -DCMAKE_CXX_COMPILER=aarch64-linux-gnu-g++";
        }
        else if (os == "osx")
        {
            args += $" -DCMAKE_OSX_ARCHITECTURES={(arch == "arm64" ? "arm64" : "x86_64")}";
        }
        else if (os == "ios")
        {
            args += " -DCMAKE_SYSTEM_NAME=iOS";
            args += arch == "arm64" ? " -DCMAKE_OSX_ARCHITECTURES=arm64" : " -DCMAKE_OSX_ARCHITECTURES=x86_64 -DCMAKE_OSX_SYSROOT=iphonesimulator";
        }
        else if (os == "maccatalyst")
        {
            args += " -DCMAKE_SYSTEM_NAME=iOS -DCMAKE_OSX_SYSROOT=macosx -DCMAKE_XCODE_ATTRIBUTE_CLANG_CXX_LIBRARY=libc++";
            args += arch == "arm64" ? " -DCMAKE_OSX_ARCHITECTURES=arm64" : " -DCMAKE_OSX_ARCHITECTURES=x86_64";
        }
        else if (os == "android")
        {
            string ndk = Environment.GetEnvironmentVariable("ANDROID_NDK_HOME");
            args += $" -DCMAKE_TOOLCHAIN_FILE=\"{ndk}/build/cmake/android.toolchain.cmake\" -DANDROID_ABI={(arch == "arm64" ? "arm64-v8a" : "x86_64")} -DANDROID_PLATFORM=android-21";
        }
        else if (os == "browser-wasm")
        {
            string emsdk = Environment.GetEnvironmentVariable("EMSDK");
            args += $" -DCMAKE_TOOLCHAIN_FILE=\"{emsdk}/upstream/emscripten/cmake/Modules/Platform/Emscripten.cmake\"";
        }

        return args;
    }

    static void RunProcess(string fileName, string args, string workingDir)
    {
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
}