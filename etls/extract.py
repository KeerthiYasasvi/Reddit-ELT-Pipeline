import psycopg2
from psycopg2.extras import execute_values
import praw
from praw import Reddit
from prawcore.exceptions import Redirect
import pandas as pd
import os
import datetime

from dotenv import load_dotenv
load_dotenv() # Load environment variables from .env file

def connect_to_reddit(client_id: str, secret_key: str, user_agent: str) -> Reddit:
    try:
        return Reddit(client_id="ClientID", client_secret="ClientSecret", user_agent="dev-app name by /u/Your_RedditUsername")
    except Exception as e:
        print("Reddit connection failed:", e)
        return None
    
def connect_to_postgres():
    """Reads connection info from environment and returns a psycopg2 connection."""
    conn = psycopg2.connect(
        host=os.getenv("POSTGRES_HOST"),
        port=os.getenv("POSTGRES_PORT"),
        dbname=os.getenv("POSTGRES_DB"),
        user=os.getenv("POSTGRES_USER"),
        password=os.getenv("POSTGRES_PASSWORD")
    )
    conn.autocommit = True
    return conn

def insert_raw_posts(conn, df: pd.DataFrame):
    """Bulk-insert the DataFrame rows into raw_posts."""
    # Convert created_utc (epoch) to timestamp
    df = df.reset_index()
    df['created_utc'] = pd.to_datetime(df['created_utc'], unit='s')
    records = df.to_dict('records')
    
    # Build insert query
    columns = list(df.columns)
    values_template = "(" + ",".join(f"%({c})s" for c in columns) + ")"
    
    with conn.cursor() as cur:
        execute_values(
            cur,
            f"INSERT INTO raw_posts ({','.join(columns)}) VALUES %s "
            "ON CONFLICT (id) DO NOTHING;",
            records,
            template=values_template
        )
        # Commit the transaction
        conn.commit()

def ensure_raw_table_exists(conn):
    with conn.cursor() as cur:
        cur.execute("""
            CREATE TABLE IF NOT EXISTS raw_posts (
              id TEXT PRIMARY KEY,
              title TEXT,
              author TEXT,
              num_comments INTEGER,
              upvote_ratio REAL,
              score INTEGER,
              created_utc TIMESTAMP,
              over_18 BOOLEAN,
              url TEXT,
              subreddit TEXT
            );
        """)

def extract_post(
                 client_id: str,
                 secret_key: str,
                 user_agent: str,
                 subreddit: str,
                 time_filter='day',
                 limit=10,
                 **kwargs):
    
    """Extract posts from a subreddit and insert them into a PostgreSQL database."""

    # 1. Extract from Reddit
    reddit = connect_to_reddit(client_id, secret_key, user_agent)
    try:
        posts = reddit.subreddit(subreddit).top(time_filter=time_filter, limit=limit)
    except Redirect:
        print(f"Subreddit '{subreddit}' not found, skipping extraction.")
        return
    keys = ('id', 'title', 'author', 'num_comments', 'upvote_ratio',
            'score', 'created_utc', 'over_18', 'url')
    data = []
    for post in posts:
        p = vars(post)
        author_name = p['author'].name if p['author'] else None
        post_dict = {
            'id': p['id'],
            'title': p['title'],
            'author': author_name,
            'num_comments': p['num_comments'],
            'upvote_ratio': p['upvote_ratio'],
            'score': p['score'],
            'created_utc': p['created_utc'],
            'over_18': p['over_18'],
            'url': p['url'],
        }
        data.append(post_dict)
    
    df = pd.DataFrame(data)
    df['subreddit'] = subreddit
    df.set_index('id', inplace=True)

    # 3. Insert into Postgres staging table
    conn = connect_to_postgres()
    ensure_raw_table_exists(conn)
    insert_raw_posts(conn, df)
    conn.close()
    print(f"Inserted {len(df)} rows into raw_posts table")