# ---------- build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Копируем весь репозиторий в контейнер
COPY . .

# Публикуем конкретный проект (файл .csproj находится в корне репо)
RUN dotnet publish "HealthCoachServer.csproj" -c Release -o /app/publish

# ---------- runtime stage ----------
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:80
EXPOSE 80

COPY --from=build /app/publish ./
ENTRYPOINT ["dotnet", "HealthCoachServer.dll"]
