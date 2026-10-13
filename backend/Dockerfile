# API .NET — imagem de produção (contexto: pasta backend/)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props .editorconfig ./
COPY src/Loteadora.Crm.SharedKernel/Loteadora.Crm.SharedKernel.csproj src/Loteadora.Crm.SharedKernel/
COPY src/Loteadora.Crm.Modules/Loteadora.Crm.Modules.csproj src/Loteadora.Crm.Modules/
COPY src/Loteadora.Crm.Api/Loteadora.Crm.Api.csproj src/Loteadora.Crm.Api/
RUN dotnet restore src/Loteadora.Crm.Api/Loteadora.Crm.Api.csproj
COPY src/ src/
RUN dotnet publish src/Loteadora.Crm.Api/Loteadora.Crm.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
EXPOSE 8080
# Pasta das chaves de Data Protection (volume): criada com o dono certo para o usuário sem privilégios.
RUN mkdir -p /keys && chown $APP_UID /keys
# Usuário sem privilégios fornecido pela imagem oficial.
USER $APP_UID
ENTRYPOINT ["dotnet", "Loteadora.Crm.Api.dll"]
