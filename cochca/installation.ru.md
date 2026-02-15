# Инструкция по развёртыванию - Production Ready

Это руководство описывает **полностью автоматизированное** развёртывание WebRTC-приложения `cochca` в Azure с соблюдением best practices безопасности.

---
## 0) Предварительные требования

- **.NET SDK 10** (со встроенной поддержкой контейнеров)
- **Azure CLI** (`az`) с аутентификацией (`az login`)
- **Docker НЕ требуется** для развёртывания
- Подписка Azure
- Домен (например `your-domain.com`) с доступом к управлению DNS

---
## 1) Локальная разработка (запуск в локальной сети)

1. Доверить сертификату разработки (Windows):
   ```powershell
   dotnet dev-certs https --trust
   ```
2. Запустить приложение:
   ```powershell
   dotnet run --project cochca --urls "https://0.0.0.0:7088;http://0.0.0.0:5247"
   ```
3. На телефоне (в той же локальной сети):
   ```
   https://<IP_компьютера>:7088
   ```
4. Принять предупреждение о сертификате (необходимо для доступа к камере/микрофону).

**Проверка:** UI должен загрузиться, кнопки подключения/чата/звонка работают локально.

---
## 2) Развёртывание одной командой

### 2.1 Редактировать `cicd/main.parameters.json`

Необходимы только базовые параметры:

```json
{
  "location": { "value": "eastus2" },
  "appName": { "value": "cochca" },
  "environmentName": { "value": "cochca-env" },
  "logAnalyticsName": { "value": "cochca-logs" },
  "containerPort": { "value": 8080 },
  "vmName": { "value": "cochca-turn" },
  "adminUsername": { "value": "azureuser" },
  "sshPublicKey": { "value": "ssh-ed25519 AAAA..." }
}
```

**Сгенерировать SSH ключ:**
```powershell
ssh-keygen -t ed25519 -C "cochca"
# Скопировать содержимое из: C:\Users\<вы>\.ssh\id_ed25519.pub
```

### 2.2 Запустить развёртывание

```powershell
cd cicd
.\deploy.ps1 -SubscriptionId "YOUR-SUBSCRIPTION-ID" -TurnDomain "turn.your-domain.com"
```

**Что произойдёт:**
1. ✅ Создаст ACR
2. ✅ Соберёт и загрузит контейнер (**Docker не нужен!**)
3. ✅ Сгенерирует безопасный пароль TURN (32 символа)
4. ✅ Развернёт всю инфраструктуру:
   - Container Apps Environment
   - Log Analytics
   - Container App (с секретами TURN в env vars)
   - VM с **авто-установкой coturn** (cloud-init)
   - VNet, NSG, Public IP
5. ✅ Настроит порты NSG (SSH, TURN)

**Длительность:** ~10-15 минут (первый раз), ~5 минут (обновления)

### 2.3 Получить выходные данные

```bash
az deployment group show -g cochca-rg -n main --query properties.outputs
```

Вы увидите:
```json
{
  "containerAppFqdn": { "value": "cochca.xxx.eastus2.azurecontainerapps.io" },
  "turnPublicIp": { "value": "20.114.168.104" }
}
```

---
## 3) Настроить DNS

Добавьте эти записи в вашем DNS-провайдере:

| Type | Name | Data | TTL |
|------|------|------|-----|
| **CNAME** | **call** | **cochca.xxx.eastus2.azurecontainerapps.io** | 1 Hour |
| **A** | **turn** | **20.114.168.104** | 1 Hour |

**Опционально:** настройте редирект `www.call.your-domain.com` → `call.your-domain.com`

---
## 4) Добавить SSL сертификат

```bash
# Добавить пользовательский домен
az containerapp hostname add -n cochca -g cochca-rg --hostname call.your-domain.com
```

Azure покажет TXT запись - добавьте её в DNS:
```
Type: TXT
Name: asuid.call  
Data: <длинный-хеш-от-azure>
TTL: 1 Hour
```

Подождите 5 минут, затем создайте сертификат:
```bash
az containerapp hostname bind -n cochca -g cochca-rg --hostname call.your-domain.com --environment cochca-env --validation-method CNAME
```

**Проверка:** `https://call.your-domain.com` работает с SSL.

---
## 5) Обновление приложения

После изменений кода:

```powershell
cd cicd
.\deploy.ps1 -SubscriptionId "YOUR-ID" -TurnDomain "turn.your-domain.com"
```

Скрипт:
1. Пересоберёт контейнер
2. Загрузит в ACR
3. Обновит Container App с новым образом

**Ноль простоя** благодаря blue-green deployment Container Apps.

---
## 6) Архитектура

```
┌─────────────────────────────────────────────────┐
│  Браузер (WebRTC)                              │
└──────────────┬──────────────────────────────────┘
               │
               │ HTTPS/WSS (SignalR)
               ▼
┌─────────────────────────────────────────────────┐
│  Container App (Blazor Server + SignalR)       │
│  - Auto SSL (managed certificates)             │
│  - Auto scaling (0-10 replicas)                │
└──────────────┬──────────────────────────────────┘
               │
               │ TURN credentials (env vars)
               ▼
┌─────────────────────────────────────────────────┐
│  TURN Server (coturn на VM)                    │
│  - Public IP для NAT traversal                 │
│  - Ports: UDP/TCP 3478, TLS 5349               │
└─────────────────────────────────────────────────┘
```

---
## 7) Проверка TURN сервера

### SSH на VM:
```bash
ssh azureuser@<turnPublicIp>
```

### Проверить статус coturn:
```bash
sudo systemctl status coturn
sudo journalctl -u coturn -n 50
```

### Тест TURN с помощью веб-инструмента:
1. Откройте https://webrtc.github.io/samples/src/content/peerconnection/trickle-ice/
2. Добавьте TURN server:
   ```
   turn:turn.your-domain.com:3478
   username: test
   password: <ваш-пароль-из-deployment>
   ```
3. Нажмите "Gather candidates"
4. Должны появиться `relay` кандидаты (это означает что TURN работает)

---
## 8) Тестирование приложения

1. Откройте `https://call.your-domain.com` на двух устройствах
2. Нажмите **Подключиться** на обоих
3. Переключитесь на вкладку **Видеозвонок** и проверьте видео/аудио
4. Проверьте отправку/приём файлов на вкладке **Чат**

Если связь не работает за строгими NAT, TURN должен помочь установить соединение.

---
## 9) Частые проблемы

### Container App не запускается
**Проверить логи:**
```bash
az containerapp logs show -n cochca -g cochca-rg --follow
```

### TURN не работает
**Проверить NSG на VM:** убедитесь, что UDP 3478 открыт
**Проверить сервис:**
```bash
ssh <user>@<turnIP>
sudo systemctl status coturn
sudo journalctl -u coturn -n 50
```

### Ошибка "InvalidAuthenticationTokenTenant"
**Причина:** неверный tenant ID
**Решение:** запустите `az login` заново или укажите правильный tenant:
```bash
az login --tenant <tenant-id>
```

### Не удаётся собрать контейнер
**Причина:** проблемы с .NET SDK publish
**Решение:** проверьте версию .NET SDK:
```bash
dotnet --version  # должна быть 10.x
```

---
## 10) Оптимизация затрат

- **Container Apps:** можно масштабировать до 0 (min replicas = 0)
  - Приложение "засыпает" когда нет трафика
  - Первый запрос займёт ~10 секунд (cold start)
  
- **VM для TURN:** остановить когда не используется
  ```bash
  az vm deallocate -g cochca-rg -n cochca-turn
  ```
  - Внимание: Public IP может измениться при повторном запуске
  
- **ACR:** тариф Basic ($5/месяц) достаточен для небольших проектов

**Примерная стоимость:**
- Container App: $0-15/месяц (зависит от использования)
- VM B1s: $10-15/месяц
- ACR Basic: $5/месяц
- Log Analytics: $2-5/месяц
**Итого: ~$20-35/месяц**

---
## 11) Безопасность

### ✅ Что уже настроено:

1. **SSL/TLS везде** - managed certificates от Azure
2. **TURN credentials** - генерируются автоматически, 32-символьный пароль
3. **Приватный ACR** - только для вашей подписки
4. **NSG правила** - только необходимые порты открыты
5. **Секреты в env vars** - не хардкодятся в коде
6. **SSH доступ** - только по ключу (пароли отключены)

### 🔒 Дополнительные меры:

**Для production:**
- Используйте Azure Key Vault для TURN credentials
- Настройте Azure Front Door для DDoS защиты
- Включите Application Insights для мониторинга
- Регулярно обновляйте VM (security patches)

---
## 12) Мониторинг

### Логи Container App:
```bash
# Streaming logs
az containerapp logs show -n cochca -g cochca-rg --follow

# Последние 100 строк
az containerapp logs show -n cochca -g cochca-rg --tail 100
```

### Метрики:
```bash
# CPU/Memory usage
az monitor metrics list --resource <container-app-id> --metric-names CpuUsage,MemoryUsage
```

### Log Analytics запросы:
Откройте Azure Portal → Log Analytics Workspace → Logs:
```kusto
ContainerAppConsoleLogs_CL
| where ContainerAppName_s == "cochca"
| order by TimeGenerated desc
| take 100
```

---
## 13) Обновление TURN пароля

1. Сгенерировать новый пароль:
   ```powershell
   $newPassword = -join ((65..90) + (97..122) + (48..57) | Get-Random -Count 32 | % {[char]$_})
   ```

2. Обновить на VM:
   ```bash
   ssh azureuser@<turnIP>
   sudo nano /etc/turnserver.conf
   # Изменить: static-auth-secret=<новый-пароль>
   sudo systemctl restart coturn
   ```

3. Обновить в Container App:
   ```bash
   az containerapp update -n cochca -g cochca-rg \
     --set-env-vars "TurnServer__Password=<новый-пароль>"
   ```

---
## 14) Backup и восстановление

### Backup VM диска:
```bash
az snapshot create \
  -g cochca-rg \
  -n cochca-turn-snapshot \
  --source /subscriptions/<sub-id>/resourceGroups/cochca-rg/providers/Microsoft.Compute/disks/cochca-turn-disk
```

### Backup конфигурации:
```bash
# Экспорт Bicep deployment
az deployment group export -g cochca-rg -n main > backup-deployment.json
```

---
## 15) Удаление всех ресурсов

⚠️ **Внимание:** это удалит ВСЁ и необратимо!

```bash
az group delete -n cochca-rg --yes --no-wait
```

---
## 16) Полезные команды

### Перезапуск Container App:
```bash
az containerapp revision restart -n cochca -g cochca-rg
```

### Список всех ресурсов:
```bash
az resource list -g cochca-rg -o table
```

### SSH на VM:
```bash
ssh azureuser@$(az vm show -g cochca-rg -n cochca-turn --show-details --query publicIps -o tsv)
```

### Статус развёртывания:
```bash
az deployment group show -g cochca-rg -n main --query properties.provisioningState
```

---
## 17) Резюме

**Автоматизация одной командой:**
```powershell
.\cicd\deploy.ps1 -SubscriptionId "YOUR-ID" -TurnDomain "turn.your-domain.com"
```

**Особенности:**
- ✅ Docker не требуется для сборки/развёртывания образа
- ✅ Автоматическое создание ACR и настройка credentials
- ✅ Полная автоматизация через Bicep (Infrastructure as Code)
- ✅ TURN-сервер для прохождения NAT
- ✅ SSL/TLS через встроенные сертификаты Container Apps
- ✅ SignalR для real-time коммуникации
- ✅ WebRTC для peer-to-peer видео/аудио

**Этапы использования:**
1. Настроить DNS (CNAME + A записи)
2. Добавить пользовательский домен + SSL сертификат на портале Azure
3. Установить coturn на VM (происходит автоматически через cloud-init)
4. Протестировать на разных сетях

**Готово!** Ваше WebRTC приложение работает в production! 🚀
