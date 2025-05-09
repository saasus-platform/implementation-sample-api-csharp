# Implementation Sample API - C#

This repository demonstrates the implementation of a SaaS application using the SaaSus SDK with **.NET Framework 4.8** and **.NET 8**.

See the documentation [API implementation using SaaS Platform](https://docs.saasus.io/ja/docs/implementation-guide/implementing-authentication-using-saasus-platform-apiserver)

## Table of Contents

1. [Steps for .NET Framework 4.8](#steps-for-net-framework-48)
2. [Steps for .NET 8](#steps-for-net-8)

## Steps for .NET Framework 4.8

### 1. Clone the Sample API Project and SaaSus SDK

1. **Clone the sample API project repository**
   ```bash
   git clone git@github.com:saasus-platform/implementation-sample-api-csharp.git
   ```

2. **Clone the SaaSus SDK repository**  
   Clone the SaaSus SDK into the same directory level as the sample API project:

   ```bash
   git clone git@github.com:saasus-platform/saasus-sdk-csharp.git
   ```

3. **Navigate to the SaaSus SDK directory**
   ```bash
   cd saasus-sdk-csharp
   ```

### 2. Build the SaaSus SDK and Generate a NuGet Package

1. **Build the SDK**  
   Build the SDK project in **Release** configuration:

   ```bash
   dotnet build -c Release
   ```

2. **Generate the NuGet package**  
   Create a NuGet package using the following command:

   ```bash
   dotnet pack -c Release
   ```

   The NuGet package file (e.g., `saasus-sdk-csharp.1.0.0.nupkg`) will be located in the `bin/Release` folder.

3. **Set up a local NuGet feed**
   - Create a directory for the local NuGet feed:
     ```bash
     mkdir C:\LocalNuGetFeed
     ```
   - Copy the generated `.nupkg` file into the local NuGet feed directory:
     ```bash
     cp bin/Release/saasus-sdk-csharp.1.0.0.nupkg C:\LocalNuGetFeed
     ```

4. **Configure the local feed in Visual Studio**
   - Open Visual Studio.
   - Navigate to **Tools → Options → NuGet Package Manager → Package Sources**.
   - Click **Add**, and set the following:
      - **Name**: `LocalFeed` (or any preferred name)
      - **Source**: `C:\LocalNuGetFeed`
   - Save the changes by clicking **OK**.

### 3. Open the .NET Framework 4.8 project in Visual Studio

1. **Open the .NET Framework 4.8 project in Visual Studio**  
   Open SampleWebAppDotNet48.sln located in implementation-sample-api-csharp/SampleWebAppDotNet48/ using Visual Studio.

2. **Install the SaaSus SDK**  
   Use the local NuGet feed to install the SaaSus SDK package:
   - In Visual Studio, open the sample API project.
   - Navigate to **Tools → NuGet Package Manager → Manage NuGet Packages for Solution**.
   - Select `LocalFeed` as the package source.
   - Search for `saasus-sdk-csharp` and install the package.

### 4. Configure the Project in Visual Studio

1. **Open Visual Studio as an Administrator**  
   Ensure you open Visual Studio with administrative privileges to configure the application URL.

2. **Set the application URL**
   - Right-click the project in Solution Explorer and select **Properties**.
   - Go to the **Web** tab.
   - Set the **Project URL** to `http://localhost:80/`.

### 5. Configure the Environment

Edit the `Web.config` file in the `SampleWebAppDotNet48` directory to set the SaaSus API credentials. Add or modify the following entries:

```xml
<configuration>
  <appSettings>
    <add key="SAASUS_SECRET_KEY" value="xxxxxxxxxx" />
    <add key="SAASUS_API_KEY" value="xxxxxxxxxx" />
    <add key="SAASUS_SAAS_ID" value="xxxxxxxxxx" />
  </appSettings>
</configuration>
```

Replace `xxxxxxxxxx` with the values from the SaaSus Admin Console.

### 6. Setup the Database (PostgreSQL)

#### Option 1: Using Docker (Recommended)

Run the following command to start the database using Docker:

```sh
docker-compose up -d
```

#### Option 2: Install PostgreSQL Manually (For EC2 Windows, No Docker Support)

1. Download **PostgreSQL 13** from the official website:  
   [PostgreSQL Windows Download](https://www.postgresql.org/download/windows/)
2. Run the installer and follow the setup instructions:
   - **Superuser Username:** `postgres` (default, cannot be changed)
   - **Superuser Password:** `postgres` (recommended for local setup)
   - **Port:** `5432` (default, leave unchanged)
3. Open **SQL Shell (psql)**:
   - Press `Windows + S` and search for **SQL Shell (psql)**.
   - Click to open it.
   - Press **Enter** for all prompts until it asks for the password.
   - Enter the **Superuser Password** set during installation.
   - You should now see:
     ```
     postgres=#
     ```
4. Run `psql` and apply `init.sql` using the full path:
   - **For .NET Framework 4.8:**
     ```sql
     \i 'C:/path/to/implementation-sample-api-csharp/SampleWebAppDotNet48/init.sql'
     ```
   - **For .NET 8:**
     ```sql
     \i 'C:/path/to/implementation-sample-api-csharp/SampleWebAppDotNet8/init.sql'
     ```

### 7. Build and Run the Project

1. Open the project in Visual Studio.
2. Build the project in **Release** or **Debug** mode.
3. Run the application.

## Steps for .NET 8

### 1. Clone the Sample API Project and SaaSus SDK

1. **Clone the sample API project repository**
   ```bash
   git clone git@github.com:saasus-platform/implementation-sample-api-csharp.git
   ```

2. **Clone the SaaSus SDK repository**  
   Clone the SaaSus SDK into the same directory level as the sample API project:

   ```bash
   git clone git@github.com:saasus-platform/saasus-sdk-csharp.git
   ```

3. **Navigate to the .NET 8 project directory**
   ```bash
   cd implementation-sample-api-csharp/SampleWebAppDotNet8
   ```

### 2. Add the SaaSus SDK as a Project Reference

Run the following command to add the SaaSus SDK as a project reference:

```bash
dotnet add SampleWebAppDotNet8.csproj reference ../../saasus-sdk-csharp/saasus-sdk-csharp.csproj
dotnet sln add ../../saasus-sdk-csharp/saasus-sdk-csharp.csproj
```

### 3. Configure the Environment

Edit the `appsettings.json` file in the `SampleWebAppDotNet8` directory to set the SaaSus API credentials. Modify it as follows:

```json
{
   "SaasusSettings": {
      "SAASUS_SECRET_KEY": "xxxxxxxxxx",
      "SAASUS_API_KEY": "xxxxxxxxxx",
      "SAASUS_SAAS_ID": "xxxxxxxxxx"
   }
}
```

Replace `xxxxxxxxxx` with the values from the SaaSus Admin Console.

### 4. Setup the Database (PostgreSQL)

Follow the same **Windows installation steps** as in the .NET Framework 4.8 section, ensuring you apply the correct `init.sql` for the respective application.

### 5. Build and Run the Project

1. Build the project:

   ```bash
   dotnet build
   ```

2. Run the project:

   ```bash
   dotnet run
   ```
