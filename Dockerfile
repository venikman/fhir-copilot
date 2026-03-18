FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/FhirCopilot.Api/FhirCopilot.Api.csproj src/FhirCopilot.Api/
RUN dotnet restore src/FhirCopilot.Api/FhirCopilot.Api.csproj

COPY src/ src/
RUN dotnet publish src/FhirCopilot.Api/FhirCopilot.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "FhirCopilot.Api.dll"]
