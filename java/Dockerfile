FROM docker.io/library/maven:3.9-eclipse-temurin-21 AS builder

WORKDIR /app

# Download dependencies first for better layer caching
COPY pom.xml .
RUN mvn dependency:go-offline -B

# Build the WAR
COPY src ./src
RUN mvn clean package -DskipTests

# Runtime stage. The sample uses Maven Cargo at runtime, so keep Maven and
# Temurin 21 from one base image instead of installing a second JDK via apk.
FROM docker.io/library/maven:3.9-eclipse-temurin-21-alpine

WORKDIR /app

# Maven runs the Cargo plugin; wget supports Docker healthchecks
RUN apk add --no-cache wget

# Create non-root user with a home directory so Maven can use ~/.m2
RUN addgroup -g 1001 -S javauser && \
    adduser -S -u 1001 -G javauser -h /home/javauser javauser

# Copy built artifacts and the cached Maven repo, owned by javauser
COPY --from=builder --chown=javauser:javauser /app/target ./target
COPY --from=builder --chown=javauser:javauser /app/pom.xml .
COPY --from=builder --chown=javauser:javauser /root/.m2 /home/javauser/.m2

USER javauser

EXPOSE 8000

CMD ["mvn", "cargo:run", "-Dquiet"]
