# Based on https://bitbucket.org/davidebbo/nugetpackages/src/1cba18b864f7c6432f8d0bb3491338921860ef88/SqlServerCompact/Tools/GetSqlCEPostBuildCmd.ps1?at=default

$solutionDir = [System.IO.Path]::GetDirectoryName($dte.Solution.FullName) + "\"
$path = $installPath.Replace($solutionDir, "`$(SolutionDir)")

$NativeAssembliesDir = Join-Path $path "NativeBinaries\*.*"

$HDF5DotNetPostBuildCmd = "xcopy /s /y `"$NativeAssembliesDir`" `"`$(TargetDir)`""
