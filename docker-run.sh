#!/bin/bash

# Docker management script for payment implementations

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

print_usage() {
    echo "Usage: $0 {build|start|stop|test|logs|clean|status}"
    echo ""
    echo "Commands:"
    echo "  build       - Build all Docker images"
    echo "  start       - Start all payment services"
    echo "  stop        - Stop all services"
    echo "  test        - Run E2E tests against containers"
    echo "  test:single - Run tests for a single implementation"
    echo "  logs        - Show logs from all services"
    echo "  clean       - Remove all containers and images"
    echo "  status      - Show status of all services"
    echo ""
    echo "Examples:"
    echo "  $0 build"
    echo "  $0 start"
    echo "  $0 test"
    echo "  $0 test:single nodejs"
    echo "  $0 logs nodejs"
}

COMPOSE="${COMPOSE:-docker compose}"
CONTAINER_CLI="${CONTAINER_CLI:-docker}"
SERVICES="nodejs php java dotnet"

check_env() {
    local services="${*:-$SERVICES}"
    local missing=0
    for service in $services; do
        if [ ! -f "$service/.env" ]; then
            echo -e "${RED}Missing $service/.env${NC}"
            echo -e "${YELLOW}Create it from $service/.env.sample and add GP-API credentials.${NC}"
            missing=1
        fi
    done
    if [ "$missing" -ne 0 ]; then
        exit 1
    fi

    echo -e "${GREEN}Environment files found${NC}"
}

build_images() {
    echo -e "${BLUE}Building Docker images...${NC}"
    $COMPOSE build --parallel $SERVICES
    echo -e "${GREEN}All images built successfully${NC}"
}

start_services() {
    echo -e "${BLUE}Starting payment services...${NC}"
    $COMPOSE up -d $SERVICES

    echo -e "${YELLOW}Waiting for services to report health...${NC}"
    $COMPOSE ps

    echo -e "${GREEN}All services started${NC}"
    echo ""
    echo "Services available at:"
    echo "  Node.js: http://localhost:8001"
    echo "  PHP:     http://localhost:8003"
    echo "  Java:    http://localhost:8004"
    echo "  .NET:    http://localhost:8006"
}

stop_services() {
    echo -e "${BLUE}Stopping all services...${NC}"
    $COMPOSE down
    echo -e "${GREEN}All services stopped${NC}"
}

run_tests() {
    echo -e "${BLUE}Running integration tests...${NC}"

    # Start services if not running
    echo -e "${YELLOW}Ensuring services are running...${NC}"
    $COMPOSE up -d $SERVICES

    # Wait for services to be healthy
    echo -e "${YELLOW}Waiting for services to be ready...${NC}"
    sleep 30

    # Run tests
    $COMPOSE --profile testing up --build --abort-on-container-exit --exit-code-from tests tests

    echo -e "${GREEN}Tests completed${NC}"
}

run_single_test() {
    local impl=$1
    if [ -z "$impl" ]; then
        echo -e "${RED}Please specify implementation: nodejs, php, java, or dotnet${NC}"
        exit 1
    fi

    case "$impl" in
        nodejs) BASE_URL=http://localhost:8001 ;;
        php) BASE_URL=http://localhost:8003 ;;
        java) BASE_URL=http://localhost:8004 ;;
        dotnet) BASE_URL=http://localhost:8006 ;;
        *)
            echo -e "${RED}Unknown implementation: $impl${NC}"
            exit 1
            ;;
    esac

    check_env "$impl"

    echo -e "${BLUE}Running tests for ${impl}...${NC}"

    # Start specific service
    $COMPOSE up -d "$impl"

    BASE_URL="$BASE_URL" bash tests/integration/run-integration-tests.sh
}

show_logs() {
    local service=$1
    if [ -z "$service" ]; then
        echo -e "${BLUE}Showing logs for all services...${NC}"
        $COMPOSE logs -f
    else
        echo -e "${BLUE}Showing logs for ${service}...${NC}"
        $COMPOSE logs -f $service
    fi
}

clean_all() {
    echo -e "${YELLOW}This will remove all containers, images, and volumes${NC}"
    read -p "Are you sure? (y/N): " -n 1 -r
    echo
    if [[ $REPLY =~ ^[Yy]$ ]]; then
        echo -e "${BLUE}Cleaning up...${NC}"
        $COMPOSE down -v --rmi all --remove-orphans
        $CONTAINER_CLI system prune -f
        echo -e "${GREEN}Cleanup completed${NC}"
    else
        echo -e "${YELLOW}Cleanup cancelled${NC}"
    fi
}

show_status() {
    echo -e "${BLUE}Service Status:${NC}"
    $COMPOSE ps
    echo ""
    echo -e "${BLUE}Images:${NC}"
    $CONTAINER_CLI images | grep -E "(payments|test)" || echo "No images found"
}

# Main script logic
case "$1" in
    build)
        build_images
        ;;
    start)
        check_env
        start_services
        ;;
    stop)
        stop_services
        ;;
    test)
        check_env
        run_tests
        ;;
    test:single)
        run_single_test $2
        ;;
    logs)
        show_logs $2
        ;;
    clean)
        clean_all
        ;;
    status)
        show_status
        ;;
    *)
        print_usage
        exit 1
        ;;
esac
