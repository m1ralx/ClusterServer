# Задача отмены задач на сервере
## ClusterClient -> RequestAllClient - клиент, который умеет отменять задачи на сервере 
## ClusterServer -> Program.cs - сервер, который умеет принимать запросы на отмену в функции, которую возвращает CreateCallback 