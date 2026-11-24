# Build: docker build -f api/Dockerfile api
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY nuget.config global.json Directory.Build.props TyHr.sln ./
COPY src/TyHr.Domain/TyHr.Domain.csproj src/TyHr.Domain/
COPY src/TyHr.Application/TyHr.Application.csproj src/TyHr.Application/
COPY src/TyHr.Infrastructure/TyHr.Infrastructure.csproj src/TyHr.Infrastructure/
COPY src/TyHr.Api/TyHr.Api.csproj src/TyHr.Api/
RUN dotnet restore src/TyHr.Api/TyHr.Api.csproj
COPY src/ src/
RUN dotnet publish src/TyHr.Api/TyHr.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# libgssapi-krb5-2: .NET/Npgsql probe for Kerberos at startup and log a missing-library error without it.
# libfontconfig1, fonts-dejavu-core: QuestPDF finds fonts for payslip PDFs.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 libfontconfig1 fonts-dejavu-core \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /keys /files && chown app:app /keys /files
WORKDIR /app
COPY --from=build /app .
USER app
# Data-protection keys; docker-compose.prod.yml mounts a volume here (owned by app, as created above).
ENV DataProtection__KeysPath=/keys
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
# The image has no curl: ask /health over bash's /dev/tcp (it answers 200 only when the database does too).
HEALTHCHECK --interval=10s --timeout=5s --start-period=30s --retries=5 \
  CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health HTTP/1.1\\r\\nHost: localhost\\r\\nConnection: close\\r\\n\\r\\n' >&3 && head -1 <&3 | grep -q ' 200 '"]
ENTRYPOINT ["dotnet", "TyHr.Api.dll"]
