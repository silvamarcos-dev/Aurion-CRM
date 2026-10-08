#!/bin/sh
# Executado só na PRIMEIRA inicialização do volume do banco.
# A aplicação usa um papel próprio, sem superusuário (menor privilégio).
set -eu
psql -v ON_ERROR_STOP=1 --username postgres <<SQL
CREATE ROLE crm_app LOGIN PASSWORD '${APP_DB_PASSWORD}';
CREATE DATABASE crm OWNER crm_app;
SQL
