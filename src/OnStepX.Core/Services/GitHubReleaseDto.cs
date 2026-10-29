using System.Runtime.Serialization;

namespace ASCOM.OnStepX.Services
{
    // Minimal GitHub releases payload shape, parsed by DataContractJsonSerializer.
    // EmitDefaultValue=true on every member so missing JSON fields land as defaults
    // instead of throwing.
    // CS0649 suppressed: fields are populated by the serializer via reflection.
#pragma warning disable 0649
    [DataContract]
    internal sealed class GitHubReleaseDto
    {
        [DataMember(Name = "tag_name", EmitDefaultValue = true)] public string tag_name;
        [DataMember(Name = "name",     EmitDefaultValue = true)] public string name;
        [DataMember(Name = "body",     EmitDefaultValue = true)] public string body;
        [DataMember(Name = "html_url", EmitDefaultValue = true)] public string html_url;
        [DataMember(Name = "draft",    EmitDefaultValue = true)] public bool   draft;
        [DataMember(Name = "prerelease", EmitDefaultValue = true)] public bool prerelease;
        [DataMember(Name = "assets",   EmitDefaultValue = true)] public GitHubReleaseAssetDto[] assets;
    }

    [DataContract]
    internal sealed class GitHubReleaseAssetDto
    {
        [DataMember(Name = "name",                 EmitDefaultValue = true)] public string name;
        [DataMember(Name = "browser_download_url", EmitDefaultValue = true)] public string browser_download_url;
        [DataMember(Name = "size",                 EmitDefaultValue = true)] public long   size;
    }
#pragma warning restore 0649
}
