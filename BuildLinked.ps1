# Set Working Directory
Split-Path $MyInvocation.MyCommand.Path | Push-Location
[Environment]::CurrentDirectory = $PWD

Remove-Item "$env:RELOADEDIIMODS/PVZ_Randomizer/*" -Force -Recurse
dotnet publish "./PVZ_Randomizer.csproj" -c Release -o "$env:RELOADEDIIMODS/PVZ_Randomizer" /p:OutputPath="./bin/Release" /p:ReloadedILLink="true"

# Restore Working Directory
Pop-Location