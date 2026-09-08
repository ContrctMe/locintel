terraform {
  required_version = ">= 1.12, < 2.0"
  required_providers {
    digitalocean = {
      source  = "digitalocean/digitalocean"
      version = "2.100.0"
    }
  }
}

# Credentials come from DIGITALOCEAN_TOKEN, never a committed variable file.
provider "digitalocean" {}

variable "campaign" {
  type = string
  validation {
    condition     = can(regex("^premise-compat-[a-z0-9-]{1,24}$", var.campaign))
    error_message = "Use a unique premise-compat- campaign name."
  }
}
variable "region" {
  type = string
}
variable "kubernetes_version" {
  type = string
}
variable "expires_at" {
  type = string
  validation {
    condition     = can(formatdate("YYYY-MM-DD", var.expires_at))
    error_message = "Supply the campaign expiry as an RFC3339 timestamp."
  }
}

resource "digitalocean_project" "campaign" {
  name        = var.campaign
  description = "Disposable Premise compatibility deployment; expires ${var.expires_at}"
  purpose     = "Operational / Developer tooling"
  environment = "Development"
}
resource "digitalocean_tag" "campaign" {
  name = var.campaign
}
resource "digitalocean_vpc" "campaign" {
  name   = var.campaign
  region = var.region
}
resource "digitalocean_kubernetes_cluster" "compat" {
  name         = var.campaign
  region       = var.region
  version      = var.kubernetes_version
  vpc_uuid     = digitalocean_vpc.campaign.id
  auto_upgrade = false
  ha           = false
  tags         = [digitalocean_tag.campaign.name]
  node_pool {
    name       = "compat"
    size       = "c-4"
    node_count = 2
    auto_scale = false
    labels     = { "premise-pool" = "compat" }
    tags       = [digitalocean_tag.campaign.name]
  }
}
resource "digitalocean_database_cluster" "postgres" {
  name                 = var.campaign
  engine               = "pg"
  version              = "17"
  size                 = "gd-2vcpu-8gb"
  node_count           = 1
  region               = var.region
  private_network_uuid = digitalocean_vpc.campaign.id
  storage_size_mib     = 30720
  tags                 = [digitalocean_tag.campaign.name]
}
resource "digitalocean_database_db" "premise" {
  cluster_id = digitalocean_database_cluster.postgres.id
  name       = "premise"
}
resource "digitalocean_database_firewall" "postgres" {
  cluster_id = digitalocean_database_cluster.postgres.id
  rule {
    type  = "k8s"
    value = digitalocean_kubernetes_cluster.compat.id
  }
}
resource "digitalocean_nfs" "keys" {
  name             = var.campaign
  region           = var.region
  size             = 50
  vpc_id           = digitalocean_vpc.campaign.id
  performance_tier = "standard"
}
resource "digitalocean_project_resources" "campaign" {
  project = digitalocean_project.campaign.id
  resources = [
    digitalocean_kubernetes_cluster.compat.urn,
    digitalocean_database_cluster.postgres.urn,
  ]
}

# No passwords, tokens or kubeconfig in ordinary outputs. Terraform state itself
# contains provider-generated credentials and must be kept private and backed up.
output "inventory" {
  value = {
    campaign      = var.campaign
    expires_at    = var.expires_at
    project_id    = digitalocean_project.campaign.id
    vpc_id        = digitalocean_vpc.campaign.id
    cluster_id    = digitalocean_kubernetes_cluster.compat.id
    database_id   = digitalocean_database_cluster.postgres.id
    database_host = digitalocean_database_cluster.postgres.private_host
    database_port = digitalocean_database_cluster.postgres.port
    nfs_id        = digitalocean_nfs.keys.id
    nfs_host      = digitalocean_nfs.keys.host
    nfs_path      = digitalocean_nfs.keys.mount_path
  }
}
