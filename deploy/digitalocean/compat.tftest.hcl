mock_provider "digitalocean" {}
variables {
  campaign           = "premise-compat-check"
  region             = "nyc3"
  kubernetes_version = "1.36.3-do.3"
  expires_at         = "2030-01-01T00:00:00Z"
}
run "bounded_private_bootstrap" {
  command = plan
  assert {
    condition     = digitalocean_kubernetes_cluster.compat.node_pool[0].node_count == 2 && !digitalocean_kubernetes_cluster.compat.node_pool[0].auto_scale
    error_message = "Compatibility must stay fixed at two nodes."
  }
  assert {
    condition     = digitalocean_database_cluster.postgres.node_count == 1 && digitalocean_database_cluster.postgres.size == "gd-2vcpu-8gb"
    error_message = "Compatibility does not provision the larger resilience database."
  }
  assert {
    condition     = digitalocean_nfs.keys.performance_tier == "standard" && digitalocean_nfs.keys.size == 50
    error_message = "Do not inherit the provider's expensive high-performance NFS default."
  }
  assert {
    condition     = length(digitalocean_database_firewall.postgres.rule) == 1 && one(digitalocean_database_firewall.postgres.rule).type == "k8s"
    error_message = "Database access is restricted to the campaign cluster."
  }
}
