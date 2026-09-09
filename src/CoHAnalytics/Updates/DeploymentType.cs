namespace CoHAnalytics.Updates;

public enum DeploymentType
{
    Unknown,
    WindowsInstaller,
    PortableSelfContained,
    PortableFrameworkDependent,
    SourceBuild
}
