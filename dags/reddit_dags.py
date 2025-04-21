import sys
import os

sys.path.append('/opt/airflow/')

import datetime
from datetime import timedelta
# The DAG object
from airflow import DAG
# Operators
from airflow.operators.python import PythonOperator
from airflow.utils.dates import days_ago
from airflow.operators.bash import BashOperator

from etls.extract import extract_post
from dotenv import load_dotenv

load_dotenv()

default_args = {
    'owner': 'Yasasvi',
    'depends_on_past': False,
    'start_date': datetime.datetime(2025,4,12),
    'email': ['airflow@example.com'],
    'email_on_failure': False,
    'email_on_retry': False,
    'retries': 2,
    'retry_delay': timedelta(minutes=2),
}


SECRET_KEY = os.getenv('secret_key')
CLIENT_ID = os.getenv('client_id')
USER_AGENT = os.getenv('user_agent')

with DAG('reddit_pipeline', default_args=default_args, schedule_interval=timedelta(hours=12), description='Reddit ELT pipeline', catchup=False) as dag:
    
    current_time = datetime.datetime.now().strftime("%Y%m%d")
    subreddit = 'ENTER_SUBREDDIT'
    file_name = f'reddit_{subreddit}_{current_time}'

    
    extract = PythonOperator(task_id='extract_data', python_callable=extract_post,
                           op_kwargs={
                               'client_id': CLIENT_ID,
                               'secret_key':SECRET_KEY,
                               'user_agent':USER_AGENT,
                               'subreddit': subreddit,
                               'time_filter': 'day',
                               'limit': 10
                           })

    dbt_transform = BashOperator(
    task_id='dbt_transform',
    bash_command=(
        'dbt run '
        '--project-dir /opt/airflow/dbt_project '
        '--profiles-dir /opt/airflow/dbt_project'
    )
    )

    extract >> dbt_transform