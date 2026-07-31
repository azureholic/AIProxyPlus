FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props AiProxy.sln ./
COPY src/AiProxy.Api/AiProxy.Api.csproj src/AiProxy.Api/
COPY src/AiProxy.Application/AiProxy.Application.csproj src/AiProxy.Application/
COPY src/AiProxy.Domain/AiProxy.Domain.csproj src/AiProxy.Domain/
COPY src/AiProxy.Infrastructure/AiProxy.Infrastructure.csproj src/AiProxy.Infrastructure/
COPY src/AiProxy.ServiceDefaults/AiProxy.ServiceDefaults.csproj src/AiProxy.ServiceDefaults/
RUN dotnet restore src/AiProxy.Api/AiProxy.Api.csproj

COPY src/ src/
RUN dotnet publish src/AiProxy.Api/AiProxy.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "AiProxy.Api.dll"]
