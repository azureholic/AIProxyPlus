FROM node:22-alpine AS frontend-build
WORKDIR /src/AIProxyPlus.Web
COPY src/AIProxyPlus.Web/package.json src/AIProxyPlus.Web/package-lock.json src/AIProxyPlus.Web/.npmrc ./
RUN npm ci
COPY src/AIProxyPlus.Web/ ./
RUN npm run build
# vite.config.ts's build.outDir ("../AIProxyPlus.Api/wwwroot") lands the build here, in this stage.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props NuGet.config AIProxyPlus.sln ./
COPY src/AIProxyPlus.Api/AIProxyPlus.Api.csproj src/AIProxyPlus.Api/
COPY src/AIProxyPlus.Application/AIProxyPlus.Application.csproj src/AIProxyPlus.Application/
COPY src/AIProxyPlus.Domain/AIProxyPlus.Domain.csproj src/AIProxyPlus.Domain/
COPY src/AIProxyPlus.Infrastructure/AIProxyPlus.Infrastructure.csproj src/AIProxyPlus.Infrastructure/
COPY src/AIProxyPlus.ServiceDefaults/AIProxyPlus.ServiceDefaults.csproj src/AIProxyPlus.ServiceDefaults/
RUN dotnet restore src/AIProxyPlus.Api/AIProxyPlus.Api.csproj --configfile NuGet.config

COPY src/ src/
# Single-container pattern (specs/iterations/iteration-3/tasks.md §Phase 2): each Docker stage has its own
# filesystem, so the frontend-build stage's output has to be copied in explicitly — publish alone won't see it.
COPY --from=frontend-build /src/AIProxyPlus.Api/wwwroot src/AIProxyPlus.Api/wwwroot
RUN dotnet publish src/AIProxyPlus.Api/AIProxyPlus.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "AIProxyPlus.Api.dll"]
