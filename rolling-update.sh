# Check if the correct number of arguments is provided
if [ "$#" -ne 3 ]; then
    echo "Usage: $0 <total_containers> <project_name> <docker_compose_file>"
    exit 1
fi

# Input parameters
total_containers="$1"
project_name="$2"
docker_compose_file="$3"

# Loop through each container
for ((i = 1; i <= total_containers; i++)); do
    container_name="$project_name-$i"

    docker compose -f "$docker_compose_file" pull &&
    docker compose -f "$docker_compose_file" stop "$container_name" &&
    docker compose -f "$docker_compose_file" rm -f "$container_name" &&
    docker compose -f "$docker_compose_file" up -d "$container_name" &&

    # Wait until the container is healthy
    while [[ "$(docker inspect -f '{{.State.Health.Status}}' $(docker compose -f "$docker_compose_file" ps -q "$container_name"))" != "healthy" ]]; do
        sleep 5
    done
done