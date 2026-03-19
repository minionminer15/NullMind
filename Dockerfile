# Build stage with .NET 8 SDK
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Copy project and restore dependencies
COPY *.csproj ./
RUN dotnet restore

# Copy all source files and build
COPY *.cs ./
RUN dotnet publish -c Release -o out

# Runtime stage with .NET 8 runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/out ./

# Start the bot
CMD ["dotnet", "NullMind.dll"]
