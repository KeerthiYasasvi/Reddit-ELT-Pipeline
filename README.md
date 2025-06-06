# Reddit ELT Pipeline

A containerized ELT workflow that extracts top posts from Reddit, loads raw data into Postgres, transforms it with dbt into a star schema, and orchestrates the entire process with Airflow.

---

## Project Description

**What this application does**  
- **Extracts** top posts from a configurable subreddit using PRAW (the Python Reddit API wrapper).  
- **Loads** the raw posts into a Postgres “raw” table, deduplicating by post ID.  
- **Transforms** the data with dbt into staging views/tables, builds author and time dimensions, a fact table, and analytical marts (e.g. author performance, hourly engagement).  
- **Orchestrates** the end‑to‑end workflow—extraction, dbt transformations, and monitoring—via Apache Airflow, scheduled at a configurable interval.

**Why these technologies**  
- **Apache Airflow** provides robust scheduling, dependency management, retries, and observability for production‑grade pipelines.  
- **dbt (Data Build Tool)** enables modular, tested SQL transformations and generates documentation—ideal for maintaining a clear star‑schema model.  
- **Postgres** offers a reliable, open‑source relational engine for both staging and final analytic tables.  
- **Docker Compose** simplifies local development by containerizing all services (Airflow, dbt, Postgres, Redis), ensuring consistent environments across machines.

---

## Architecture

```text
[ Airflow Scheduler ]   ──▶ [ extract_post (PythonOperator) ] ──▶ [ raw_posts (Postgres) ]
                           │
                           └─▶ [ dbt_transform (BashOperator) ] ──▶ [ dbt (stg → dims → fact → marts) ] ──▶ [ Postgres ]
```
---

## Table of Contents

- [Prerequisites](#prerequisites)
- [How to Install and Run the Project](#how-to-install-and-run-the-project)
- [Future Improvements](#future-improvements)

---

## Prerequisites

Before you begin, make sure you have:

- **Docker** & **Docker Compose** installed and running  
- **Git** (to clone the repository)  
- A **Reddit developer account** with:
  - `client_id`
  - `secret_key`
  - `user_agent`  
- (Optional) **psql** or **pgAdmin** if you want to inspect the Postgres database directly

---

## How to Install and Run the Project

1. **Clone the repository**
   
   ```bash
   git clone https://github.com/KeerthiYasasvi/reddit_data_pipeline.git
   cd reddit_data_pipeline

2. **Configure environment variables**
   
   Copy the example file and open .env in your editor and Fill in your Reddit API credentials and Postgres password in the .env file:
   
     ```bash
     cp .env.example .env

3. **Enter the subreddit name in the DAG file**

      ```
        with DAG('reddit_pipeline', default_args=default_args, schedule_interval=timedelta(hours=12), description='Reddit ELT pipeline', catchup=False) as dag:
        
        current_time = datetime.datetime.now().strftime("%Y%m%d")
        subreddit = 'ENTER_SUBREDDIT_NAME_HERE'
        file_name = f'reddit_{subreddit}_{current_time}'
      ```
   
4. **Configure your extract script**

   In etls/extract.py, ensure the PRAW client uses your credentials. It should read from environment variables:

   ```
   def extract_post(...):
    reddit = Reddit(
        client_id=os.getenv("client_id"),
        client_secret=os.getenv("secret_key"),
        user_agent=os.getenv("user_agent")
    )
    ```

5. **Verify Postgres settings in Docker Compose:**

   Open docker-compose.yml and under the postgres: service, confirm:

    ```
    POSTGRES_USER: postgres
    POSTGRES_PASSWORD: YOUR_POSTGRES_PASSWORD
    POSTGRES_DB: reddit_pipeline
    ```

6.  **Update your dbt profiles:**

    In profiles.yml, make sure the target connection matches:

    ```
    outputs:
      dev:
        type: postgres
        host: postgres
        port: 5432
        user: postgres
        password: YOUR_POSTGRES_PASSWORD
        dbname: reddit_pipeline
        schema: public
    target: dev
    ```
    
7.    **Start all services**
  
      Build the Docker images and launch:

        ```docker-compose up --build -d```

9. **Initialize Airflow**
   
    The airflow-init service will automatically run the database migrations and create the default admin/admin user on first start. Wait a minute for it to complete, then stop and restart the stack:
   
      ```
      docker-compose down
      docker-compose up -d
      ```

11. **Verify the setup**
   
    Airflow UI runs on: http://localhost:8080 (login: admin / paswword: admin)
    dbt debug:
   
      ```bash
    docker-compose run dbt dbt debug --profiles-dir /usr/app
   
    docker exec -it reddit_pipeline-postgres-1 psql -U postgres -d reddit_pipeline -c "\dt"

12. **Trigger the pipeline**
    
    Airflow: Enable and trigger the reddit_pipeline DAG in the UI. (it will run every 12 hours by default)
    Change schedule_interval, timedelta in the DAG file from: minutes, hours, days, weeks
   
    ```with DAG('reddit_pipeline', default_args=default_args, **schedule_interval=timedelta(hours=12)**, description='Reddit ELT pipeline', catchup=False) as dag:```

---

## Future Improvements

- **Incremental Models**  
    Convert large staging and fact models to `materialized='incremental'` so each run only processes new rows, dramatically reducing build time on growing datasets.

- **dbt Tests & Documentation**  
    Add schema and data tests (uniqueness, not_null, relationships) in `schema.yml`, generate a dbt docs site (`dbt docs generate`), and host the documentation alongside your code for easy exploration.

- **Airflow Enhancements**  
  • Configure Slack or email alerts on task or DAG failures  
  • Add SLA checks for downstream models  
  • Parameterize the DAG via Airflow Variables or Connections (e.g. subreddit name, schedule interval)

---

**Tips for Recreating Locally**

- Ensure **Docker Desktop** (or Docker Engine) and **Docker Compose** are running before starting the stack.    
- If you hit module‑import errors in Airflow (e.g. `No module named 'etls'`), verify that `etls/` has an `__init__.py` and is correctly mounted under `/opt/airflow/etls`.  
