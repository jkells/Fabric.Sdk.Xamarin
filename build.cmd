@echo off

msbuild /p:Configuration=Release /target:rebuild Sources\Fabric.sln
nuget pack NuGet\Fabric.nuspec -BasePath .
nuget pack NuGet\Crashlytics.nuspec -BasePath .