# implementation-sample-api-csharp

This is a SaaS implementation sample using the SaaSus SDK.

See the documentation [API implementation using SaaS Platform](https://docs.saasus.io/docs/implementing-authentication-using-saasus-platform-apiserver).

1. Clone the repository:
   ```bash
   git clone git@github.com:Anti-Pattern-Inc/implementation-sample-api-csharp.git
   ```

2. Set environment variables in `Web.config` located at `SampleWebApplication`:
   ```xml
   <add key="SaasusSecretKey" value="your_secret_key" />
   <add key="SaasusApiKey" value="your_api_key" />
   <add key="SaasusSaasId" value="your_saas_id" />
   ```

3. Download `saasus-sdk-csharp.dll` from [this link](https://github.com/Anti-Pattern-Inc/saasus-sdk-csharp/blob/main/obj/Debug/saasus-sdk-csharp.dll).

4. In Visual Studio, add a reference to the `saasus-sdk-csharp.dll` file:
   - Right-click the project in Solution Explorer -> Add -> Reference -> Browse -> Select the `saasus-sdk-csharp.dll`.

5. Open Visual Studio **as an administrator**, and set the application URL to `http://localhost:80/`:
   - Right-click the project -> Properties -> Web -> Set the project URL to `http://localhost:80/`.

6. Build and run the project.

**Note:** Ensure that Visual Studio is run with **administrator privileges** whenever using port 80 for local development.
```