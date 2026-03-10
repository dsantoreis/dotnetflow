# Build API
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS api-build
WORKDIR /src
COPY api/DotnetFlow.slnx api/
COPY api/src/DotnetFlow.Api/DotnetFlow.Api.csproj api/src/DotnetFlow.Api/
RUN dotnet restore api/DotnetFlow.slnx
COPY api/ api/
RUN dotnet publish api/src/DotnetFlow.Api/DotnetFlow.Api.csproj -c Release -o /app/publish --no-restore

# Build frontend
FROM node:22-alpine AS frontend-build
WORKDIR /frontend
COPY frontend/package.json frontend/package-lock.json* ./
RUN npm install
COPY frontend/ .
RUN npm run build

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview
WORKDIR /app
COPY --from=api-build /app/publish .
COPY --from=frontend-build /frontend/dist ./wwwroot
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=3s CMD wget -qO- http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "DotnetFlow.Api.dll"]
