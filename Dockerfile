# build stage
FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src

COPY *.sln ./
COPY HealthCoachServer/*.csproj HealthCoachServer/
RUN dotnet restore

COPY . .
RUN dotnet publish HealthCoachServer -c Release -o /app/publish

# runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:80
EXPOSE 80
COPY --from=build /app/publish ./
ENTRYPOINT ["dotnet", "HealthCoachServer.dll"]
