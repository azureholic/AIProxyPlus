FROM node:22-alpine AS frontend-build
WORKDIR /src/AiProxy.Web
COPY src/AiProxy.Web/package.json src/AiProxy.Web/package-lock.json ./
RUN npm ci
COPY src/AiProxy.Web/ ./
RUN npm run build
# vite.config.ts's build.outDir ("../AiProxy.Api/wwwroot") lands the build here, in this stage.

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
# Single-container pattern (specs/iterations/iteration-3/tasks.md §Phase 2): each Docker stage has its own
# filesystem, so the frontend-build stage's output has to be copied in explicitly — publish alone won't see it.
COPY --from=frontend-build /src/AiProxy.Api/wwwroot src/AiProxy.Api/wwwroot
RUN dotnet publish src/AiProxy.Api/AiProxy.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "AiProxy.Api.dll"]
