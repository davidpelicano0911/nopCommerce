#!/bin/bash

# Define colors for terminal output
GREEN='\033[0;32m'
BLUE='\033[0;34m'
RED='\033[0;31m'
NC='\033[0m' # No Color

echo -e "${BLUE}================================================================${NC}"
echo -e "${BLUE}  Automation: Database Reset & Load Test Execution             ${NC}"
echo -e "${BLUE}================================================================${NC}"
echo ""

# Step 1: Execute SQL inside the Docker container
echo -e "${GREEN}[1/3] Applying inventory rules (reset_db.sql) to SQL Server...${NC}"

# Injecting the SQL file using docker exec
docker exec -i nopcommerce_mssql_server /opt/mssql-tools18/bin/sqlcmd -S localhost -U SA -P "nopCommerce_db_password" -C -d nopcommerce_db < reset_db.sql

if [ $? -eq 0 ]; then
  echo -e "${GREEN}✓ Rules applied successfully!${NC}"
else
  echo -e "${RED}An error occurred in the SQL script. Trying fallback path...${NC}"
  docker exec -i nopcommerce_mssql_server /opt/mssql-tools/bin/sqlcmd -S localhost -U SA -P "nopCommerce_db_password" -C -d nopcommerce_db < reset_db.sql
fi

echo ""

# Step 2: Clear nopCommerce Cache (Restarting the web container)
echo -e "${GREEN}[2/3] Restarting nopCommerce to clear inventory cache...${NC}"
docker restart nopcommerce > /dev/null

echo -n "Waiting for server to be back online"
# Ping homepage until it returns HTTP 200
until $(curl --output /dev/null --silent --head --fail http://localhost/); do
  printf '.'
  sleep 2
done
echo -e " ${GREEN}Online!${NC}"
echo ""

# Step 3: Execute the k6 loadtest
echo -e "${GREEN}[3/3] Starting k6 load test...${NC}"
k6 run loadtest.js

echo ""
echo -e "${BLUE}================================================================${NC}"
echo -e "${BLUE}  Test Completed! Check the updated charts in your Grafana.     ${NC}"
echo -e "${BLUE}================================================================${NC}"