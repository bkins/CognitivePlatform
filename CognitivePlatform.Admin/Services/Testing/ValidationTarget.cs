namespace CognitivePlatform.Admin.Services.Testing;

/// <summary>Server-owned, immutable expectations for the accepted deployment pair.</summary>
public sealed record ValidationTarget(string Environment, int Port, string ApiVersion, string ApiCommit,
    string ApiArtifact, string ApiDeployment, string ApiInventorySha256, string Database,
    string WindowsVersion, string WindowsArtifact, string WindowsDeployment, string WindowsManifestSha256,
    string ApiEnvironmentSha256, string ApiLauncherSha256)
{
    public static ValidationTarget ForProfile(string profile)
    {
        var environment = profile switch
        {
            "runtime-dev" or "deployment-dev" => "Dev",
            "runtime-prod" or "deployment-prod" => "Prod",
            _ => throw new ArgumentException("Unknown validation target.")
        };
        var windowsVersion = environment == "Dev" ? "1.3.0.129" : "1.3.0.130";
        return new(environment, environment == "Dev" ? 5273 : 5275, "1.3.0.127", "16ede3968d2fb9d530c2f24a6af112737438e4b3",
            @"C:\CP\Artifacts\API\1.3.0.127", $@"C:\CP\Deploy\API\{environment}",
            "1FFB1C80C5D838FE4AC38CED6F3807DAC5D4DF43634A64EBBC0DAE291C7D8072",
            $@"C:\CP\Data\{(environment == "Dev" ? "Development" : "Prod")}\platform.db",
            windowsVersion, $@"C:\CP\Artifacts\LaaWindows\{windowsVersion}", $@"C:\CP\Deploy\LaaWindows\{environment}",
            environment == "Dev" ? "B228B1F01160C4876E1E2B1A485D835B4609E894187C578614C6467AAFFB4B88" : "91B942E9C028F0AB8B36A79BE1AA1727FE0F3ECE3015E6863AFAE52704EC7958",
            environment == "Dev" ? "4236872719348F200C07A26FFD4B2E35B09C9429A75796A2B6B4A068D8C8688B" : "7F4ACAF0EF03D9CCADA28F05A2C0A13B5A803279881A3C72286D92639B3D13C7",
            environment == "Dev" ? "F50C5097B4B5D1CFA802A59977D1B6E56EE123DBAAC774C33ECE00A7CC46A107" : "6207B11D0485909DA18E9114787AE1C8CBED9AB1ACD4D3163AD234774A5F32F1");
    }
}
