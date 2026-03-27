FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

# Copy solution and project files first for layer caching
COPY SynapseHealth.sln .
COPY src/SynapseHealth.OrderRouter/SynapseHealth.OrderRouter.csproj src/SynapseHealth.OrderRouter/
COPY tests/SynapseHealth.OrderRouter.Tests/SynapseHealth.OrderRouter.Tests.csproj tests/SynapseHealth.OrderRouter.Tests/
RUN dotnet restore

# Copy everything and build
COPY . .
RUN dotnet publish src/SynapseHealth.OrderRouter -c Release -o /app/publish

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview
WORKDIR /app
COPY --from=build /app/publish .
COPY data/ ./data/
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "SynapseHealth.OrderRouter.dll"]
