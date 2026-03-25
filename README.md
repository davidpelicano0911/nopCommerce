

# nopCommerce Observability Project - Assignment 1

This repository contains the observability instrumentation for the nopCommerce "Custom Places an order" flow using **OpenTelemetry**, **Grafana**, **Prometheus**, and **Jaeger**.

### Architecture Diagram
The observability instrumentation is integrated into nopCommerce using a **layered, in-process architecture** combined with a **centralized telemetry pipeline**. This ensures that telemetry is captured at every stage of the "Customer Places an Order" flow while maintaining strict data privacy and decoupling the application from the storage backends.

![Architecture Diagram](docs/architecture.png)

## How to Run the Project

### 1. Start the Full Stack (Docker)
Make sure you are in the root of the project and run:

```bash
docker compose up --build -d
```

*This command starts nopCommerce, SQL Server, OTel Collector, Jaeger, Prometheus, and Grafana.*

### 2. Initial Setup

1. Wait until all services are healthy (`docker ps`).
2. Go to `http://localhost` to complete the nopCommerce setup (if running for the first time).
3. Access Grafana at `http://localhost:3000` (dashboards are pre-configured).

### 3. Run the Load Test (k6)

To generate data and visualize metrics in the dashboard, run:

```bash
./assessment/load-test/run_loadtest.sh
```

*Note: This script resets the stock state in the database and starts k6 with 40 virtual users.*

---

## Observability Interfaces

* **Web Store:** [http://localhost](http://localhost)
* **Grafana (Dashboards):** [http://localhost:3000](http://localhost:3000)
* **Jaeger (Traces):** [http://localhost:16686](http://localhost:16686)
* **Prometheus (Raw Metrics):** [http://localhost:9090](http://localhost:9090)

---


## Repository Organization (Submission)

To facilitate evaluation, the documentation and evidence are organized as follows:

* **Technical Report - [REPORT.md](REPORT.md)** – Detailed implementation report.
* **Critical Analysis - [CRITIQUE.md](CRITIQUE.md)** – Critical analysis and architectural decisions.
* **Architecture Analysis - [ANALYSIS.md](ANALYSIS.md)** – Instrumentation flow details.
* **Visual Evidence (./assessment/evidence/)** – Screenshots from Jaeger, Prometheus, and PII redaction.
* **Dashboards (./assessment/dashboards/screenshots/)** – Grafana dashboard visualizations.
* **Configurations (./assessment/observability/)** – YAML files (OTel, Prometheus, Grafana).
* **Load Test Scripts (./assessment/load-test/)** – k6 scripts and SQL reset files.


---
David Poeta Pelicano
-
NMEC: 113391