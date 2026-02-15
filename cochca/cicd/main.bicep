targetScope = 'resourceGroup'

@description('Azure region')
param location string = resourceGroup().location

@description('Container App name')
param appName string

@description('Container Apps environment name')
param environmentName string

@description('Log Analytics workspace name')
param logAnalyticsName string

@description('Container image (e.g. ghcr.io/org/app:tag)')
param containerImage string

@description('Container registry server (e.g. myregistry.azurecr.io). Leave empty for public images.')
param registryServer string = ''

@description('Container registry username (required for private registries)')
param registryUsername string = ''

@description('Container registry password (required for private registries)')
@secure()
param registryPassword string = ''

@description('Container port')
param containerPort int = 8080

@description('TURN server secret for coturn authentication')
@secure()
param turnPassword string

@description('Domain for TURN server (e.g. turn.coch.ca)')
param turnDomain string = ''


@description('CPU cores for the container')
param containerCpu string = '0.5'

@description('Memory for the container')
param containerMemory string = '1.0Gi'

@description('TURN VM name')
param vmName string

@description('TURN VM size')
param vmSize string = 'Standard_B1s'

@description('Admin username for the VM')
param adminUsername string

@description('SSH public key for the VM')
param sshPublicKey string

@description('VNet name')
param vnetName string = 'cochca-vnet'

@description('Subnet name')
param subnetName string = 'default'

@description('Public IP name for TURN')
param publicIpName string = 'turn-pip'

@description('NSG name')
param nsgName string = 'turn-nsg'

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource managedEnv 'Microsoft.App/managedEnvironments@2023-05-01' = {
  name: environmentName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: listKeys(logAnalytics.id, logAnalytics.apiVersion).primarySharedKey
      }
    }
  }
}

resource containerApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: appName
  location: location
  properties: {
    managedEnvironmentId: managedEnv.id
    configuration: {
      registries: registryServer == '' ? [] : [
        {
          server: registryServer
          username: registryUsername
          passwordSecretRef: 'registry-password'
        }
      ]
      secrets: registryServer == '' ? [] : [
        {
          name: 'registry-password'
          value: registryPassword
        }
        {
          name: 'turn-password'
          value: turnPassword
        }
      ]
      ingress: {
        external: true
        targetPort: containerPort
        transport: 'auto'
      }
    }
    template: {
      containers: [
        {
          name: appName
          image: containerImage
          resources: {
            cpu: json(containerCpu)
            memory: containerMemory
          }
          env: [
            {
              name: 'ASPNETCORE_URLS'
              value: 'http://0.0.0.0:${containerPort}'
            }
            {
              name: 'TurnServer__Password'
              secretRef: 'turn-password'
            }
            {
              name: 'TurnServer__Domain'
              value: turnDomain
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 1
      }
    }
  }
}

resource vnet 'Microsoft.Network/virtualNetworks@2023-11-01' = {
  name: vnetName
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [
        '10.10.0.0/16'
      ]
    }
    subnets: [
      {
        name: subnetName
        properties: {
          addressPrefix: '10.10.1.0/24'
        }
      }
    ]
  }
}

resource publicIp 'Microsoft.Network/publicIPAddresses@2023-11-01' = {
  name: publicIpName
  location: location
  sku: {
    name: 'Standard'
  }
  properties: {
    publicIPAllocationMethod: 'Static'
  }
}

resource nsg 'Microsoft.Network/networkSecurityGroups@2023-11-01' = {
  name: nsgName
  location: location
  properties: {
    securityRules: [
      {
        name: 'Allow-SSH'
        properties: {
          priority: 100
          access: 'Allow'
          direction: 'Inbound'
          protocol: 'Tcp'
          sourcePortRange: '*'
          destinationPortRange: '22'
          sourceAddressPrefix: '*'
          destinationAddressPrefix: '*'
        }
      }
      {
        name: 'Allow-TURN-TCP'
        properties: {
          priority: 200
          access: 'Allow'
          direction: 'Inbound'
          protocol: 'Tcp'
          sourcePortRange: '*'
          destinationPortRanges: [
            '3478'
            '5349'
          ]
          sourceAddressPrefix: '*'
          destinationAddressPrefix: '*'
        }
      }
      {
        name: 'Allow-TURN-UDP'
        properties: {
          priority: 210
          access: 'Allow'
          direction: 'Inbound'
          protocol: 'Udp'
          sourcePortRange: '*'
          destinationPortRange: '3478'
          sourceAddressPrefix: '*'
          destinationAddressPrefix: '*'
        }
      }
    ]
  }
}

resource nic 'Microsoft.Network/networkInterfaces@2023-11-01' = {
  name: '${vmName}-nic'
  location: location
  properties: {
    ipConfigurations: [
      {
        name: 'ipconfig1'
        properties: {
          subnet: {
            id: vnet.properties.subnets[0].id
          }
          privateIPAllocationMethod: 'Dynamic'
          publicIPAddress: {
            id: publicIp.id
          }
        }
      }
    ]
    networkSecurityGroup: {
      id: nsg.id
    }
  }
}

resource vm 'Microsoft.Compute/virtualMachines@2023-09-01' = {
  name: vmName
  location: location
  properties: {
    hardwareProfile: {
      vmSize: vmSize
    }
    osProfile: {
      computerName: vmName
      adminUsername: adminUsername
      customData: base64('''
#!/bin/bash
set -e

# Wait for cloud-init to finish
cloud-init status --wait

# Install coturn
apt-get update
DEBIAN_FRONTEND=noninteractive apt-get install -y coturn

# Configure coturn
cat > /etc/turnserver.conf <<EOF
listening-port=3478
tls-listening-port=5349
fingerprint
use-auth-secret
static-auth-secret=${TURN_PASSWORD}
realm=${TURN_DOMAIN}
total-quota=100
stale-nonce=600
no-stdout-log
log-file=/var/log/turnserver.log
simple-log
EOF

# Set TURN password and domain from metadata
TURN_PASSWORD="''') + turnPassword + '''"
TURN_DOMAIN="''') + (empty(turnDomain) ? 'turn.example.com' : turnDomain) + '''"

sed -i "s/\${TURN_PASSWORD}/$TURN_PASSWORD/g" /etc/turnserver.conf
sed -i "s/\${TURN_DOMAIN}/$TURN_DOMAIN/g" /etc/turnserver.conf

# Enable and start coturn
sed -i 's/#TURNSERVER_ENABLED=1/TURNSERVER_ENABLED=1/' /etc/default/coturn
systemctl enable coturn
systemctl start coturn

echo "Coturn installed and configured successfully"
''')
      linuxConfiguration: {
        disablePasswordAuthentication: true
        ssh: {
          publicKeys: [
            {
              path: '/home/${adminUsername}/.ssh/authorized_keys'
              keyData: sshPublicKey
            }
          ]
        }
      }
    }
    storageProfile: {
      imageReference: {
        publisher: 'Canonical'
        offer: '0001-com-ubuntu-server-jammy'
        sku: '22_04-lts'
        version: 'latest'
      }
      osDisk: {
        createOption: 'FromImage'
      }
    }
    networkProfile: {
      networkInterfaces: [
        {
          id: nic.id
        }
      ]
    }
  }
}

output containerAppFqdn string = containerApp.properties.configuration.ingress.fqdn
output turnPublicIp string = publicIp.properties.ipAddress
