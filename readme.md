# AspNetCore.CloudFlare

This project is **not** official by CloudFlare

||HCaptcha.AspNetCore|
|-|-|
|NuGet|[![NuGet](https://img.shields.io/nuget/v/BenjaminAbt.AspNetCore.CloudFlare)](https://www.nuget.org/packages/BenjaminAbt.AspNetCore.CloudFlare/)|
License|[![GitHub](https://img.shields.io/github/license/benjaminabt/AspNetCore.CloudFlare)](LICENSE)
|GitHub Build|![NETCore](https://github.com/BenjaminAbt/AspNetCore.CloudFlare/workflows/NETCore/badge.svg)|

## Usage with ASP.NET Core

```cs
public void ConfigureServices(IServiceCollection services)
{
   // ...
   
   services.AddCloudFlareForwardHeaderOptions(o => { /* optional changes here */});
   
   // ...
}

public void Configure(IApplicationBuilder app)
{
   // ...
   
   app.UseCloudFlareForwardHeader();
   
   // ...
}
```

## Donation

Please donate - if possible - to necessary institutions of your choice such as child cancer aid, children's hospices etc.
Thanks!

## License

[MIT License](LICENSE)
