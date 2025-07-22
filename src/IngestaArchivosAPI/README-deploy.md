# Deployment Guide

## Scripts Created

### 1. `deploy.sh` - Main deployment script
Automatiza todo el proceso de deployment via SSH.

```bash
# Uso básico
./deploy.sh

# Parámetros
./deploy.sh [environment] [server_user] [server_host] [branch]
```

### 2. `server-setup.sh` - Server preparation script
Ejecutar UNA VEZ en el servidor para prepararlo.

```bash
# En el servidor
sudo ./server-setup.sh
```

## Deployment Process

### Primera vez (Setup del servidor):
1. Subir `server-setup.sh` al servidor
2. Ejecutar: `sudo ./server-setup.sh`
3. Clonar el repositorio en `/opt/LSI_AI_CASHPOSTER_API`
4. Configurar variables en `.env`

### Deployments regulares:
```bash
# Instalar sshpass (primera vez)
sudo apt-get install sshpass

# Desde tu máquina local
./deploy.sh
```

## Variables de Entorno

Crear `.env` en el servidor con:
```env
OPENAI_API_KEY=tu-api-key-real
ASPNETCORE_ENVIRONMENT=Production
```

## Troubleshooting

### Verificar estado:
```bash
ssh root@174.138.117.37
cd /opt/LSI_AI_CASHPOSTER_API/backend/src/IngestaArchivosAPI
docker-compose ps
docker-compose logs
```

### Reconstruir completamente:
```bash
docker-compose down
docker-compose up -d --build --force-recreate
```