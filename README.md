Native libheif binaries for .NET (Includes libde265 and dav1d)


For iOS

If not used `DllImport("__Internal")` , but using `NativeLibrary.SetDllImportResolver` `NativeLibrary.GetMainProgramHandle`
you can add this to the csproj:
```
<PropertyGroup Condition="$(TargetFramework.Contains('-ios')) Or $(TargetFramework.StartsWith('Xamarin.iOS'))">
	<_ExportSymbolsExplicitly>false</_ExportSymbolsExplicitly>
	<MtouchExtraArgs>-gcc_flags -v</MtouchExtraArgs>
</PropertyGroup>
```